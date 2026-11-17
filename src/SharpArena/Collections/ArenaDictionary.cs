using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SharpArena.Allocators;

namespace SharpArena.Collections;

/// <summary>
/// Metadata describing the shared state of an <see cref="ArenaDictionary{TKey, TValue}"/>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ArenaDictionaryHeader
{
    public int Count;
    public int Capacity; // Must be power of 2
    public int GrowThreshold; // Count at/above which the table grows before the next insert; always < Capacity
    public int* Buckets; // 1-based indices (0 = empty)
    public uint* Hashes; // Contiguous cached (mixed) hash per entry, parallel to Keys
    public void* Keys;   // Contiguous TKey*
    public void* Values; // Contiguous TValue*
}

/// <summary>
/// A high-performance, arena-backed, add-only dictionary for unmanaged types.
/// Uses a Structure of Arrays (SoA) layout for optimal cache density during probing.
/// </summary>
/// <typeparam name="TKey">The unmanaged key type.</typeparam>
/// <typeparam name="TValue">The unmanaged value type.</typeparam>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct ArenaDictionary<TKey, TValue> : IDictionary<TKey, TValue>, IReadOnlyDictionary<TKey, TValue>
    where TKey : unmanaged, IEquatable<TKey>
    where TValue : unmanaged
{
    private readonly ArenaAllocator _arena;
    private readonly int _generation;
    internal ArenaDictionaryHeader* _header;

    // A table with fewer than 4 buckets fills up completely under LoadFactor rounding (e.g.
    // capacity 1 or 2 means every bucket must be occupied before growth is even considered),
    // which turns a lookup miss in FindBucket into an infinite loop since it never finds an
    // empty bucket to stop at.
    private const int MinCapacity = 4;

    // Bounds the `capacity <<= 1` doubling loop below so it can never overflow into 0 or negative
    // and spin forever when a caller passes an enormous initialCapacity.
    private const int MaxCapacity = 1 << 30;

    /// <summary>
    /// Initializes a new instance of the <see cref="ArenaDictionary{TKey, TValue}"/> struct.
    /// </summary>
    /// <param name="arena">The allocator providing unmanaged storage.</param>
    /// <param name="initialCapacity">The initial capacity of the dictionary. Will be rounded up to the nearest power of two.</param>
    public ArenaDictionary(ArenaAllocator arena, int initialCapacity = 16)
    {
        _arena = arena ?? throw new ArgumentNullException(nameof(arena));
        _generation = arena.CurrentGeneration;

        if (initialCapacity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(initialCapacity));
        }

        if (initialCapacity > MaxCapacity)
        {
            throw new ArgumentOutOfRangeException(nameof(initialCapacity), $"initialCapacity must not exceed {MaxCapacity}.");
        }

        int capacity = MinCapacity;
        while (capacity < initialCapacity) capacity <<= 1;

        _header = (ArenaDictionaryHeader*)arena.Alloc((nuint)sizeof(ArenaDictionaryHeader), align: 8);
        _header->Count = 0;
        Resize(capacity, copyCount: 0);
    }

    /// <summary>
    /// Computes the count at/above which the table must grow before the next insert, so that at
    /// least one bucket is always guaranteed to be empty (otherwise a lookup miss never terminates).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ComputeGrowThreshold(int capacity)
    {
        int threshold = (int)((long)capacity * 7 / 10);
        return threshold >= capacity ? capacity - 1 : threshold;
    }

    /// <summary>
    /// Allocates a fresh table of <paramref name="newCap"/> buckets, copies the first
    /// <paramref name="copyCount"/> dense entries over and rebuilds the bucket index from the
    /// cached hashes (no key is re-hashed).
    /// </summary>
    private void Resize(int newCap, int copyCount)
    {
        int* newBuckets = (int*)_arena.Alloc((nuint)newCap * sizeof(int), align: 16);
        uint* newHashes = (uint*)_arena.Alloc((nuint)newCap * sizeof(uint), align: 16);
        TKey* newKeys = (TKey*)_arena.Alloc((nuint)newCap * (nuint)sizeof(TKey), align: (nuint)UnsafeHelpers.AlignOf<TKey>());
        TValue* newValues = (TValue*)_arena.Alloc((nuint)newCap * (nuint)sizeof(TValue), align: (nuint)UnsafeHelpers.AlignOf<TValue>());

        new Span<int>(newBuckets, newCap).Clear();

        if (copyCount > 0)
        {
            Buffer.MemoryCopy(_header->Hashes, newHashes, (nuint)newCap * sizeof(uint), (nuint)copyCount * sizeof(uint));
            Buffer.MemoryCopy(_header->Keys, newKeys, (nuint)newCap * (nuint)sizeof(TKey), (nuint)copyCount * (nuint)sizeof(TKey));
            Buffer.MemoryCopy(_header->Values, newValues, (nuint)newCap * (nuint)sizeof(TValue), (nuint)copyCount * (nuint)sizeof(TValue));
            OpenAddressing.Rebuild(newBuckets, newHashes, copyCount, (uint)newCap - 1);
        }

        _header->Capacity = newCap;
        _header->GrowThreshold = ComputeGrowThreshold(newCap);
        _header->Buckets = newBuckets;
        _header->Hashes = newHashes;
        _header->Keys = newKeys;
        _header->Values = newValues;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private readonly void CheckAlive() =>
        UnsafeHelpers.CheckAliveThrowIfNot(_arena, _generation, nameof(ArenaDictionary<,>));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private readonly int FindBucket(in TKey key, uint hash) =>
        OpenAddressing.FindSlot(_header->Buckets, _header->Hashes, (TKey*)_header->Keys, (uint)_header->Capacity - 1, key, hash);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private readonly int FindEntry(in TKey key) =>
        _header->Buckets[FindBucket(key, Hashing.Hash(key))] - 1;

    /// <summary>
    /// Gets the number of elements contained in the <see cref="ArenaDictionary{TKey, TValue}"/>.
    /// </summary>
    public readonly int Count
    {
        get
        {
            CheckAlive();
            return _header->Count;
        }
    }

    /// <summary>
    /// Gets a value indicating whether the <see cref="ArenaDictionary{TKey, TValue}"/> is read-only.
    /// </summary>
    public readonly bool IsReadOnly => false;

    /// <summary>
    /// Gets or sets the element with the specified key.
    /// </summary>
    /// <param name="key">The key of the element to get or set.</param>
    /// <returns>The element with the specified key.</returns>
    /// <exception cref="KeyNotFoundException">The property is retrieved and <paramref name="key"/> is not found.</exception>
    public TValue this[TKey key]
    {
        readonly get => TryGetValue(key, out var value) ?
            value : throw new KeyNotFoundException();
        set => Insert(key, value, overwrite: true);
    }

    /// <summary>
    /// Gets an <see cref="ICollection{TKey}"/> containing the keys of the <see cref="ArenaDictionary{TKey, TValue}"/>.
    /// </summary>
    public ICollection<TKey> Keys => new KeyCollection(this);

    /// <summary>
    /// Gets an <see cref="ICollection{TValue}"/> containing the values in the <see cref="ArenaDictionary{TKey, TValue}"/>.
    /// </summary>
    public ICollection<TValue> Values => new ValueCollection(this);

    IEnumerable<TKey> IReadOnlyDictionary<TKey, TValue>.Keys => Keys;
    IEnumerable<TValue> IReadOnlyDictionary<TKey, TValue>.Values => Values;

    /// <summary>
    /// Adds an element with the provided key and value to the <see cref="ArenaDictionary{TKey, TValue}"/>.
    /// </summary>
    /// <param name="key">The object to use as the key of the element to add.</param>
    /// <param name="value">The object to use as the value of the element to add.</param>
    /// <exception cref="ArgumentException">An element with the same key already exists in the <see cref="ArenaDictionary{TKey, TValue}"/>.</exception>
    public void Add(TKey key, TValue value)
    {
        if (!TryAdd(key, value))
        {
            throw new ArgumentException("An item with the same key has already been added.");
        }
    }

    /// <summary>
    /// Attempts to add the specified key and value to the dictionary.
    /// </summary>
    /// <param name="key">The key of the element to add.</param>
    /// <param name="value">The value of the element to add.</param>
    /// <returns><see langword="true"/> if the key/value pair was added to the dictionary successfully; <see langword="false"/> if the key already exists.</returns>
    public bool TryAdd(TKey key, TValue value) => Insert(key, value, overwrite: false);

    /// <returns><see langword="true"/> when a new entry was added.</returns>
    private bool Insert(in TKey key, in TValue value, bool overwrite)
    {
        CheckAlive();

        uint hash = Hashing.Hash(key);
        int bucketIdx = FindBucket(key, hash);
        int entryIdx = _header->Buckets[bucketIdx] - 1;
        if (entryIdx >= 0)
        {
            if (overwrite)
            {
                ((TValue*)_header->Values)[entryIdx] = value;
            }

            return false;
        }

        if (_header->Count >= _header->GrowThreshold)
        {
            Grow();
            bucketIdx = FindBucket(key, hash);
        }

        int newEntryIdx = _header->Count++;
        _header->Buckets[bucketIdx] = newEntryIdx + 1;
        _header->Hashes[newEntryIdx] = hash;
        ((TKey*)_header->Keys)[newEntryIdx] = key;
        ((TValue*)_header->Values)[newEntryIdx] = value;
        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void Grow()
    {
        int oldCap = _header->Capacity;
        if (oldCap >= MaxCapacity)
        {
            throw new InvalidOperationException("ArenaDictionary capacity overflow.");
        }

        Resize(oldCap * 2, _header->Count);
    }

    /// <summary>
    /// Determines whether the <see cref="ArenaDictionary{TKey, TValue}"/> contains an element with the specified key.
    /// </summary>
    /// <param name="key">The key to locate in the <see cref="ArenaDictionary{TKey, TValue}"/>.</param>
    /// <returns><see langword="true"/> if the <see cref="ArenaDictionary{TKey, TValue}"/> contains an element with the key; otherwise, <see langword="false"/>.</returns>
    public readonly bool ContainsKey(TKey key)
    {
        CheckAlive();
        return FindEntry(key) >= 0;
    }

    /// <summary>
    /// Specialized ContainsKey for string types using ReadOnlySpan{char} to avoid allocations.
    /// Supports cross-encoding for ArenaUtf8String.
    /// </summary>
    /// <param name="key">The key to locate in the <see cref="ArenaDictionary{TKey, TValue}"/>.</param>
    /// <returns><see langword="true"/> if the <see cref="ArenaDictionary{TKey, TValue}"/> contains an element with the key; otherwise, <see langword="false"/>.</returns>
    public readonly bool ContainsKey(ReadOnlySpan<char> key)
    {
        CheckAlive();
        return OpenAddressing.FindEntry(_header->Buckets, _header->Hashes, (TKey*)_header->Keys, (uint)_header->Capacity - 1, key) >= 0;
    }

    /// <summary>
    /// Specialized ContainsKey for ArenaUtf8String using ReadOnlySpan{byte} to avoid allocations.
    /// Supports cross-encoding for ArenaUtf16String.
    /// </summary>
    /// <param name="key">The key to locate in the <see cref="ArenaDictionary{TKey, TValue}"/>.</param>
    /// <returns><see langword="true"/> if the <see cref="ArenaDictionary{TKey, TValue}"/> contains an element with the key; otherwise, <see langword="false"/>.</returns>
    public readonly bool ContainsKey(ReadOnlySpan<byte> key)
    {
        CheckAlive();
        return OpenAddressing.FindEntry(_header->Buckets, _header->Hashes, (TKey*)_header->Keys, (uint)_header->Capacity - 1, key) >= 0;
    }

    /// <summary>
    /// Gets the value associated with the specified key.
    /// </summary>
    /// <param name="key">The key whose value to get.</param>
    /// <param name="value">When this method returns, the value associated with the specified key, if the key is found; otherwise, the default value for the type of the <paramref name="value"/> parameter. This parameter is passed uninitialized.</param>
    /// <returns><see langword="true"/> if the <see cref="ArenaDictionary{TKey, TValue}"/> contains an element with the specified key; otherwise, <see langword="false"/>.</returns>
    public readonly bool TryGetValue(TKey key, out TValue value)
    {
        CheckAlive();
        return TryGetValueAt(FindEntry(key), out value);
    }

    /// <summary>
    /// Specialized TryGetValue for string types using ReadOnlySpan{char} to avoid allocations.
    /// Supports cross-encoding for ArenaUtf8String.
    /// </summary>
    /// <param name="key">The key whose value to get.</param>
    /// <param name="value">When this method returns, the value associated with the specified key, if the key is found; otherwise, the default value for the type of the <paramref name="value"/> parameter. This parameter is passed uninitialized.</param>
    /// <returns><see langword="true"/> if the <see cref="ArenaDictionary{TKey, TValue}"/> contains an element with the specified key; otherwise, <see langword="false"/>.</returns>
    public readonly bool TryGetValue(ReadOnlySpan<char> key, out TValue value)
    {
        CheckAlive();
        return TryGetValueAt(
            OpenAddressing.FindEntry(_header->Buckets, _header->Hashes, (TKey*)_header->Keys, (uint)_header->Capacity - 1, key),
            out value);
    }

    /// <summary>
    /// Specialized TryGetValue for ArenaUtf8String using ReadOnlySpan{byte} to avoid allocations.
    /// Supports cross-encoding for ArenaUtf16String.
    /// </summary>
    /// <param name="key">The key whose value to get.</param>
    /// <param name="value">When this method returns, the value associated with the specified key, if the key is found; otherwise, the default value for the type of the <paramref name="value"/> parameter. This parameter is passed uninitialized.</param>
    /// <returns><see langword="true"/> if the <see cref="ArenaDictionary{TKey, TValue}"/> contains an element with the specified key; otherwise, <see langword="false"/>.</returns>
    public readonly bool TryGetValue(ReadOnlySpan<byte> key, out TValue value)
    {
        CheckAlive();
        return TryGetValueAt(
            OpenAddressing.FindEntry(_header->Buckets, _header->Hashes, (TKey*)_header->Keys, (uint)_header->Capacity - 1, key),
            out value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private readonly bool TryGetValueAt(int entryIdx, out TValue value)
    {
        if (entryIdx >= 0)
        {
            value = ((TValue*)_header->Values)[entryIdx];
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>
    /// Removes all keys and values from the <see cref="ArenaDictionary{TKey, TValue}"/>.
    /// </summary>
    public void Clear()
    {
        CheckAlive();
        new Span<int>(_header->Buckets, _header->Capacity).Clear();
        _header->Count = 0;
    }

    /// <summary>
    /// Shrinks the logical capacity of the hash table down to fit its current contents and
    /// rebuilds the bucket index, which improves probe locality.
    /// </summary>
    /// <remarks>
    /// This does not reduce the arena's actual memory footprint: the arena is a bump allocator
    /// with no general free(), so the previous buckets/keys/values buffers stay allocated (and
    /// unreachable) in the segment they came from. This can make <see cref="ArenaAllocator.AllocatedBytes"/>
    /// go up rather than down. Call this to speed up future lookups after a burst of inserts
    /// followed by removals-via-<see cref="Clear"/>, not to reclaim memory.
    /// </remarks>
    public void TrimExcess()
    {
        CheckAlive();
        int count = _header->Count;
        int newCap = MinCapacity;
        while (ComputeGrowThreshold(newCap) < count) newCap <<= 1;
        if (newCap >= _header->Capacity) return;

        Resize(newCap, count);
    }

    /// <summary>
    /// Removes the element with the specified key from the <see cref="ArenaDictionary{TKey, TValue}"/>. Not supported.
    /// </summary>
    public bool Remove(TKey key) => throw new NotSupportedException();
    void ICollection<KeyValuePair<TKey, TValue>>.Add(KeyValuePair<TKey, TValue> item) => Add(item.Key, item.Value);
    readonly bool ICollection<KeyValuePair<TKey, TValue>>.Contains(KeyValuePair<TKey, TValue> item)
    {
        if (TryGetValue(item.Key, out var value))
        {
            return EqualityComparer<TValue>.Default.Equals(value, item.Value);
        }
        return false;
    }
    void ICollection<KeyValuePair<TKey, TValue>>.CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
    {
        if (array == null) throw new ArgumentNullException(nameof(array));
        if (arrayIndex < 0 || arrayIndex > array.Length) throw new ArgumentOutOfRangeException(nameof(arrayIndex));
        if (array.Length - arrayIndex < Count) throw new ArgumentException("Destination array too small.");
        CheckAlive();
        int count = _header->Count;
        TKey* keys = (TKey*)_header->Keys;
        TValue* values = (TValue*)_header->Values;
        for (int i = 0; i < count; i++)
        {
            array[arrayIndex + i] = new KeyValuePair<TKey, TValue>(keys[i], values[i]);
        }
    }
    bool ICollection<KeyValuePair<TKey, TValue>>.Remove(KeyValuePair<TKey, TValue> item) => throw new NotSupportedException();

    /// <summary>
    /// Returns an enumerator that iterates through the <see cref="ArenaDictionary{TKey, TValue}"/>.
    /// </summary>
    /// <returns>A struct enumerator for the <see cref="ArenaDictionary{TKey, TValue}"/>. A <c>foreach</c> loop binds to
    /// this method directly and never boxes, unlike the <see cref="IEnumerable{T}"/> interface below.</returns>
    public readonly Enumerator GetEnumerator() => new Enumerator(this);
    readonly IEnumerator<KeyValuePair<TKey, TValue>> IEnumerable<KeyValuePair<TKey, TValue>>.GetEnumerator() => GetEnumerator();
    readonly IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>
    /// Enumerates the elements of an <see cref="ArenaDictionary{TKey, TValue}"/>.
    /// </summary>
    public unsafe struct Enumerator : IEnumerator<KeyValuePair<TKey, TValue>>
    {
        private readonly ArenaDictionary<TKey, TValue> _dict;
        private int _index;
        private KeyValuePair<TKey, TValue> _current;

        internal Enumerator(ArenaDictionary<TKey, TValue> dict)
        {
            _dict = dict;
            _index = -1;
            _current = default;
        }

        /// <summary>
        /// Advances the enumerator to the next element of the <see cref="ArenaDictionary{TKey, TValue}"/>.
        /// </summary>
        /// <returns><see langword="true"/> if the enumerator was successfully advanced to the next element; <see langword="false"/> if the enumerator has passed the end of the collection.</returns>
        public bool MoveNext()
        {
            _dict.CheckAlive();
            var header = _dict._header;
            if (header == null) return false;
            if (++_index < header->Count)
            {
                _current = new KeyValuePair<TKey, TValue>(((TKey*)header->Keys)[_index], ((TValue*)header->Values)[_index]);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Gets the element at the current position of the enumerator.
        /// </summary>
        public readonly KeyValuePair<TKey, TValue> Current => _current;

        /// <summary>
        /// Gets the element at the current position of the enumerator.
        /// </summary>
        readonly object IEnumerator.Current => _current;

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        public void Dispose() { }

        /// <summary>
        /// Sets the enumerator to its initial position, which is before the first element in the collection.
        /// </summary>
        public void Reset() => _index = -1;
    }

    private struct KeyCollection : ICollection<TKey>
    {
        private readonly ArenaDictionary<TKey, TValue> _dict;
        public KeyCollection(ArenaDictionary<TKey, TValue> dict) => _dict = dict;
        public readonly int Count => _dict.Count;
        public readonly bool IsReadOnly => true;
        public void Add(TKey item) => throw new NotSupportedException();
        public void Clear() => throw new NotSupportedException();
        public readonly bool Contains(TKey item) => _dict.ContainsKey(item);
        public void CopyTo(TKey[] array, int arrayIndex)
        {
            int count = _dict.Count;
            unsafe
            {
                TKey* keys = (TKey*)_dict._header->Keys;
                for (int i = 0; i < count; i++) array[arrayIndex + i] = keys[i];
            }
        }
        public bool Remove(TKey item) => throw new NotSupportedException();
        public readonly KeyEnumerator GetEnumerator() => new KeyEnumerator(_dict);
        readonly IEnumerator<TKey> IEnumerable<TKey>.GetEnumerator() => GetEnumerator();
        readonly IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public struct KeyEnumerator : IEnumerator<TKey>
        {
            private readonly ArenaDictionary<TKey, TValue> _dict;
            private int _index;
            private TKey _current;

            internal KeyEnumerator(ArenaDictionary<TKey, TValue> dict)
            {
                _dict = dict;
                _index = -1;
                _current = default;
            }

            public bool MoveNext()
            {
                _dict.CheckAlive();
                var header = _dict._header;
                if (header == null) return false;
                if (++_index < header->Count)
                {
                    _current = ((TKey*)header->Keys)[_index];
                    return true;
                }
                return false;
            }

            public readonly TKey Current => _current;
            readonly object IEnumerator.Current => _current;
            public void Dispose() { }
            public void Reset() => _index = -1;
        }
    }

    private struct ValueCollection : ICollection<TValue>
    {
        private readonly ArenaDictionary<TKey, TValue> _dict;
        public ValueCollection(ArenaDictionary<TKey, TValue> dict) => _dict = dict;
        public int Count => _dict.Count;
        public bool IsReadOnly => true;
        public void Add(TValue item) => throw new NotSupportedException();
        public void Clear() => throw new NotSupportedException();
        public bool Contains(TValue item)
        {
            int count = _dict.Count;
            unsafe
            {
                TValue* values = (TValue*)_dict._header->Values;
                for (int i = 0; i < count; i++) if (EqualityComparer<TValue>.Default.Equals(values[i], item)) return true;
            }
            return false;
        }
        public void CopyTo(TValue[] array, int arrayIndex)
        {
            int count = _dict.Count;
            unsafe
            {
                TValue* values = (TValue*)_dict._header->Values;
                for (int i = 0; i < count; i++) array[arrayIndex + i] = values[i];
            }
        }
        public bool Remove(TValue item) => throw new NotSupportedException();
        public ValueEnumerator GetEnumerator() => new ValueEnumerator(_dict);
        IEnumerator<TValue> IEnumerable<TValue>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public unsafe struct ValueEnumerator : IEnumerator<TValue>
        {
            private readonly ArenaDictionary<TKey, TValue> _dict;
            private int _index;
            private TValue _current;

            internal ValueEnumerator(ArenaDictionary<TKey, TValue> dict)
            {
                _dict = dict;
                _index = -1;
                _current = default;
            }

            public bool MoveNext()
            {
                _dict.CheckAlive();
                var header = _dict._header;
                if (header == null) return false;
                if (++_index < header->Count)
                {
                    _current = ((TValue*)header->Values)[_index];
                    return true;
                }
                return false;
            }

            public TValue Current => _current;
            object IEnumerator.Current => _current;
            public void Dispose() { }
            public void Reset() => _index = -1;
        }
    }
}
