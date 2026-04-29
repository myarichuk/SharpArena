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
    public int* Buckets; // 1-based indices (0 = empty)
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

    private const float LoadFactor = 0.7f;

    /// <summary>
    /// Initializes a new instance of the <see cref="ArenaDictionary{TKey, TValue}"/> struct.
    /// </summary>
    public ArenaDictionary(ArenaAllocator arena, int initialCapacity = 16)
    {
        _arena = arena ?? throw new ArgumentNullException(nameof(arena));
        _generation = arena.CurrentGeneration;

        int capacity = 1;
        while (capacity < initialCapacity) capacity <<= 1;

        _header = (ArenaDictionaryHeader*)arena.Alloc((nuint)sizeof(ArenaDictionaryHeader), align: 8);
        _header->Count = 0;
        _header->Capacity = capacity;

        AllocateTable(capacity);
    }

    private void AllocateTable(int capacity)
    {
        nuint bucketsSize = (nuint)capacity * sizeof(int);
        nuint keysSize = (nuint)capacity * (nuint)sizeof(TKey);
        nuint valuesSize = (nuint)capacity * (nuint)sizeof(TValue);

        _header->Buckets = (int*)_arena.Alloc(bucketsSize, align: 16);
        _header->Keys = _arena.Alloc(keysSize, align: (nuint)UnsafeHelpers.AlignOf<TKey>());
        _header->Values = _arena.Alloc(valuesSize, align: (nuint)UnsafeHelpers.AlignOf<TValue>());

        new Span<int>(_header->Buckets, capacity).Clear();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private readonly void CheckAlive() => 
        UnsafeHelpers.CheckAliveThrowIfNot(_arena, _generation, nameof(ArenaDictionary<,>));

    private int FindBucket(TKey key)
    {
        uint capacity = (uint)_header->Capacity;
        int* buckets = _header->Buckets;
        TKey* keys = (TKey*)_header->Keys;
        uint mask = capacity - 1;
        uint hash = Hashing.Hash(key);
        uint index = hash & mask;

        while (true)
        {
            int entryIdxPlusOne = buckets[index];
            if (entryIdxPlusOne == 0) return (int)index;

            if (keys[entryIdxPlusOne - 1].Equals(key)) return (int)index;

            index = (index + 1) & mask;
        }
    }

    public int Count
    {
        get
        {
            CheckAlive();
            return _header->Count;
        }
    }

    public bool IsReadOnly => false;

    public TValue this[TKey key]
    {
        get => TryGetValue(key, out var value) ? 
            value : throw new KeyNotFoundException();
        set => AddOrUpdate(key, value);
    }

    public ICollection<TKey> Keys => new KeyCollection(this);
    public ICollection<TValue> Values => new ValueCollection(this);

    IEnumerable<TKey> IReadOnlyDictionary<TKey, TValue>.Keys => Keys;
    IEnumerable<TValue> IReadOnlyDictionary<TKey, TValue>.Values => Values;

    public void Add(TKey key, TValue value)
    {
        if (!TryAdd(key, value))
        {
            throw new ArgumentException("An item with the same key has already been added.");
        }
    }

    public bool TryAdd(TKey key, TValue value)
    {
        CheckAlive();
        if (_header->Count >= _header->Capacity * LoadFactor)
        {
            Grow();
        }

        int bucketIdx = FindBucket(key);
        int entryIdxPlusOne = _header->Buckets[bucketIdx];
        if (entryIdxPlusOne != 0) return false;

        int newEntryIdx = _header->Count++;
        _header->Buckets[bucketIdx] = newEntryIdx + 1;
        ((TKey*)_header->Keys)[newEntryIdx] = key;
        ((TValue*)_header->Values)[newEntryIdx] = value;
        return true;
    }

    private void AddOrUpdate(TKey key, TValue value)
    {
        CheckAlive();
        if (_header->Count >= _header->Capacity * LoadFactor)
        {
            Grow();
        }

        int bucketIdx = FindBucket(key);
        int entryIdxPlusOne = _header->Buckets[bucketIdx];
        if (entryIdxPlusOne == 0)
        {
            int newEntryIdx = _header->Count++;
            _header->Buckets[bucketIdx] = newEntryIdx + 1;
            ((TKey*)_header->Keys)[newEntryIdx] = key;
            ((TValue*)_header->Values)[newEntryIdx] = value;
        }
        else
        {
            ((TValue*)_header->Values)[entryIdxPlusOne - 1] = value;
        }
    }

    private void Grow()
    {
        int oldCap = _header->Capacity;
        int newCap = oldCap * 2;
        int count = _header->Count;
        TKey* oldKeys = (TKey*)_header->Keys;
        TValue* oldValues = (TValue*)_header->Values;

        int* newBuckets = (int*)_arena.Alloc((nuint)newCap * sizeof(int), align: 16);
        TKey* newKeys = (TKey*)_arena.Alloc((nuint)newCap * (nuint)sizeof(TKey), align: (nuint)UnsafeHelpers.AlignOf<TKey>());
        TValue* newValues = (TValue*)_arena.Alloc((nuint)newCap * (nuint)sizeof(TValue), align: (nuint)UnsafeHelpers.AlignOf<TValue>());

        new Span<int>(newBuckets, newCap).Clear();
        Unsafe.CopyBlockUnaligned(newKeys, oldKeys, (uint)(count * sizeof(TKey)));
        Unsafe.CopyBlockUnaligned(newValues, oldValues, (uint)(count * sizeof(TValue)));

        _header->Capacity = newCap;
        _header->Buckets = newBuckets;
        _header->Keys = newKeys;
        _header->Values = newValues;

        uint mask = (uint)newCap - 1;
        for (int i = 0; i < count; i++)
        {
            uint hash = Hashing.Hash(newKeys[i]);
            uint idx = hash & mask;
            while (newBuckets[idx] != 0) idx = (idx + 1) & mask;
            newBuckets[idx] = i + 1;
        }
    }

    public bool ContainsKey(TKey key)
    {
        CheckAlive();
        int bucketIdx = FindBucket(key);
        return _header->Buckets[bucketIdx] != 0;
    }

    /// <summary>
    /// Specialized ContainsKey for ArenaString using ReadOnlySpan{char} to avoid allocations.
    /// </summary>
    public bool ContainsKey(ReadOnlySpan<char> key)
    {
        if (typeof(TKey) != typeof(ArenaString)) return false;
        CheckAlive();
        
        uint capacity = (uint)_header->Capacity;
        int* buckets = _header->Buckets;
        ArenaString* keys = (ArenaString*)_header->Keys;
        uint mask = capacity - 1;
        uint hash = Hashing.HashString(key);
        uint index = hash & mask;

        while (true)
        {
            int entryIdxPlusOne = buckets[index];
            if (entryIdxPlusOne == 0) return false;
            if (keys[entryIdxPlusOne - 1].Equals(key)) return true;
            index = (index + 1) & mask;
        }
    }

    public bool TryGetValue(TKey key, out TValue value)
    {
        CheckAlive();
        int bucketIdx = FindBucket(key);
        int entryIdxPlusOne = _header->Buckets[bucketIdx];
        if (entryIdxPlusOne != 0)
        {
            value = ((TValue*)_header->Values)[entryIdxPlusOne - 1];
            return true;
        }
        value = default;
        return false;
    }

    /// <summary>
    /// Specialized TryGetValue for ArenaString using ReadOnlySpan{char} to avoid allocations.
    /// </summary>
    public bool TryGetValue(ReadOnlySpan<char> key, out TValue value)
    {
        if (typeof(TKey) != typeof(ArenaString))
        {
            value = default;
            return false;
        }
        CheckAlive();

        uint capacity = (uint)_header->Capacity;
        int* buckets = _header->Buckets;
        ArenaString* keys = (ArenaString*)_header->Keys;
        TValue* values = (TValue*)_header->Values;
        uint mask = capacity - 1;
        uint hash = Hashing.HashString(key);
        uint index = hash & mask;

        while (true)
        {
            int entryIdxPlusOne = buckets[index];
            if (entryIdxPlusOne == 0) break;
            if (keys[entryIdxPlusOne - 1].Equals(key))
            {
                value = values[entryIdxPlusOne - 1];
                return true;
            }
            index = (index + 1) & mask;
        }

        value = default;
        return false;
    }

    public void Clear()
    {
        CheckAlive();
        new Span<int>(_header->Buckets, _header->Capacity).Clear();
        _header->Count = 0;
    }

    /// <summary>
    /// Reduces memory usage and optimizes the hash table.
    /// </summary>
    public void TrimExcess()
    {
        CheckAlive();
        int count = _header->Count;
        if (count == 0)
        {
            _header->Capacity = 1;
            AllocateTable(1);
            return;
        }

        int newCap = 1;
        while (newCap < count / LoadFactor) newCap <<= 1;
        if (newCap >= _header->Capacity) return;

        TKey* oldKeys = (TKey*)_header->Keys;
        TValue* oldValues = (TValue*)_header->Values;

        int* newBuckets = (int*)_arena.Alloc((nuint)newCap * sizeof(int), align: 16);
        TKey* newKeys = (TKey*)_arena.Alloc((nuint)newCap * (nuint)sizeof(TKey), align: (nuint)UnsafeHelpers.AlignOf<TKey>());
        TValue* newValues = (TValue*)_arena.Alloc((nuint)newCap * (nuint)sizeof(TValue), align: (nuint)UnsafeHelpers.AlignOf<TValue>());

        new Span<int>(newBuckets, newCap).Clear();
        Unsafe.CopyBlockUnaligned(newKeys, oldKeys, (uint)(count * sizeof(TKey)));
        Unsafe.CopyBlockUnaligned(newValues, oldValues, (uint)(count * sizeof(TValue)));

        _header->Capacity = newCap;
        _header->Buckets = newBuckets;
        _header->Keys = newKeys;
        _header->Values = newValues;

        uint mask = (uint)newCap - 1;
        for (int i = 0; i < count; i++)
        {
            uint hash = Hashing.Hash(newKeys[i]);
            uint idx = hash & mask;
            while (newBuckets[idx] != 0) idx = (idx + 1) & mask;
            newBuckets[idx] = i + 1;
        }
    }

    public bool Remove(TKey key) => throw new NotSupportedException();
    void ICollection<KeyValuePair<TKey, TValue>>.Add(KeyValuePair<TKey, TValue> item) => Add(item.Key, item.Value);
    bool ICollection<KeyValuePair<TKey, TValue>>.Contains(KeyValuePair<TKey, TValue> item)
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

    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() => new Enumerator(this);
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

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

        public KeyValuePair<TKey, TValue> Current => _current;
        object IEnumerator.Current => _current;
        public void Dispose() { }
        public void Reset() => _index = -1;
    }

    private struct KeyCollection : ICollection<TKey>
    {
        private readonly ArenaDictionary<TKey, TValue> _dict;
        public KeyCollection(ArenaDictionary<TKey, TValue> dict) => _dict = dict;
        public int Count => _dict.Count;
        public bool IsReadOnly => true;
        public void Add(TKey item) => throw new NotSupportedException();
        public void Clear() => throw new NotSupportedException();
        public bool Contains(TKey item) => _dict.ContainsKey(item);
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
        public IEnumerator<TKey> GetEnumerator() => new KeyEnumerator(_dict);
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        private unsafe struct KeyEnumerator : IEnumerator<TKey>
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

            public TKey Current => _current;
            object IEnumerator.Current => _current;
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
        public IEnumerator<TValue> GetEnumerator() => new ValueEnumerator(_dict);
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        private unsafe struct ValueEnumerator : IEnumerator<TValue>
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
