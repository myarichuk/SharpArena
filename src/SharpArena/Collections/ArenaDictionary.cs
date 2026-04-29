using System.Collections;
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
    public void* Keys;
    public void* Values;
    public ulong* Bitset;
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
    private ArenaDictionaryHeader* _header;

    private const float LoadFactor = 0.7f;

    /// <summary>
    /// Initializes a new instance of the <see cref="ArenaDictionary{TKey, TValue}"/> struct.
    /// </summary>
    /// <param name="arena">The allocator to use.</param>
    /// <param name="initialCapacity">The initial capacity (will be rounded up to the next power of 2).</param>
    public ArenaDictionary(ArenaAllocator arena, int initialCapacity = 16)
    {
        _arena = arena ?? throw new ArgumentNullException(nameof(arena));
        _generation = arena.CurrentGeneration;

        int capacity = 1;
        while (capacity < initialCapacity) capacity <<= 1;

        _header = (ArenaDictionaryHeader*)arena.Alloc((nuint)sizeof(ArenaDictionaryHeader), align: (nuint)IntPtr.Size);
        _header->Count = 0;
        _header->Capacity = capacity;

        AllocateTable(capacity);
    }

    private void AllocateTable(int capacity)
    {
        int bitsetWords = (capacity + 63) / 64;
        nuint keysSize = (nuint)capacity * (nuint)sizeof(TKey);
        nuint valuesSize = (nuint)capacity * (nuint)sizeof(TValue);
        nuint bitsetSize = (nuint)bitsetWords * sizeof(ulong);

        // Align bitset and values properly
        nuint keyAlign = (nuint)UnsafeHelpers.AlignOf<TKey>();
        nuint valAlign = (nuint)UnsafeHelpers.AlignOf<TValue>();
        
        // We allocate all blocks in one go to keep them close in memory
        void* keysBlock = _arena.Alloc(keysSize, align: keyAlign);
        void* valuesBlock = _arena.Alloc(valuesSize, align: valAlign);
        void* bitsetBlock = _arena.Alloc(bitsetSize, align: 8);

        _header->Keys = keysBlock;
        _header->Values = valuesBlock;
        _header->Bitset = (ulong*)bitsetBlock;

        new Span<ulong>(_header->Bitset, bitsetWords).Clear();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private readonly void CheckAlive() => 
        UnsafeHelpers.CheckAliveThrowIfNot(_arena, _generation, nameof(ArenaDictionary<,>));

    private int GetSlot(TKey key, int capacity, ulong* bitset, TKey* keys)
    {
        uint hash = (uint)key.GetHashCode();
        uint mask = (uint)capacity - 1;
        uint index = hash & mask;

        while (true)
        {
            if (!IsSlotOccupied(bitset, (int)index))
            {
                return (int)index;
            }

            if (keys[index].Equals(key))
            {
                return (int)index;
            }

            index = (index + 1) & mask;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsSlotOccupied(ulong* bitset, int index) => 
        (bitset[index >> 6] & (1UL << (index & 63))) != 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void SetSlotOccupied(ulong* bitset, int index) => 
        bitset[index >> 6] |= 1UL << (index & 63);

    /// <inheritdoc cref="IDictionary" />
    public int Count
    {
        get
        {
            CheckAlive();
            return _header->Count;
        }
    }

    /// <inheritdoc />
    public bool IsReadOnly => false;

    /// <inheritdoc cref="IDictionary{TKey, TValue}" />
    public TValue this[TKey key]
    {
        get => TryGetValue(key, out var value) ? 
            value : throw new KeyNotFoundException();
        set => AddOrUpdate(key, value);
    }

    /// <inheritdoc />
    public ICollection<TKey> Keys => new KeyCollection(this);

    /// <inheritdoc />
    public ICollection<TValue> Values => new ValueCollection(this);

    IEnumerable<TKey> IReadOnlyDictionary<TKey, TValue>.Keys => Keys;
    IEnumerable<TValue> IReadOnlyDictionary<TKey, TValue>.Values => Values;

    /// <inheritdoc />
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
    public bool TryAdd(TKey key, TValue value)
    {
        CheckAlive();
        if (_header->Count >= _header->Capacity * LoadFactor)
        {
            Grow();
        }

        int slot = GetSlot(key, _header->Capacity, _header->Bitset, (TKey*)_header->Keys);
        if (IsSlotOccupied(_header->Bitset, slot))
        {
            return false;
        }

        SetSlotOccupied(_header->Bitset, slot);
        ((TKey*)_header->Keys)[slot] = key;
        ((TValue*)_header->Values)[slot] = value;
        _header->Count++;
        return true;
    }

    private void AddOrUpdate(TKey key, TValue value)
    {
        CheckAlive();
        if (_header->Count >= _header->Capacity * LoadFactor)
        {
            Grow();
        }

        int slot = GetSlot(key, _header->Capacity, _header->Bitset, (TKey*)_header->Keys);
        if (!IsSlotOccupied(_header->Bitset, slot))
        {
            SetSlotOccupied(_header->Bitset, slot);
            ((TKey*)_header->Keys)[slot] = key;
            _header->Count++;
        }
        ((TValue*)_header->Values)[slot] = value;
    }

    private void Grow()
    {
        int oldCap = _header->Capacity;
        int newCap = oldCap * 2;
        void* oldKeys = _header->Keys;
        void* oldValues = _header->Values;
        ulong* oldBitset = _header->Bitset;

        AllocateTable(newCap);
        _header->Capacity = newCap;
        _header->Count = 0;

        for (int i = 0; i < oldCap; i++)
        {
            if (IsSlotOccupied(oldBitset, i))
            {
                AddInternal(((TKey*)oldKeys)[i], ((TValue*)oldValues)[i]);
            }
        }
    }

    private void AddInternal(TKey key, TValue value)
    {
        int slot = GetSlot(key, _header->Capacity, _header->Bitset, (TKey*)_header->Keys);
        SetSlotOccupied(_header->Bitset, slot);
        ((TKey*)_header->Keys)[slot] = key;
        ((TValue*)_header->Values)[slot] = value;
        _header->Count++;
    }

    /// <inheritdoc cref="IDictionary{TKey,TValue}" />
    public bool ContainsKey(TKey key)
    {
        CheckAlive();
        int slot = GetSlot(key, _header->Capacity, _header->Bitset, (TKey*)_header->Keys);
        return IsSlotOccupied(_header->Bitset, slot);
    }

    /// <inheritdoc cref="IDictionary{TKey,TValue}" />
    public bool Remove(TKey key) => throw new NotSupportedException("ArenaDictionary does not support removal.");

    /// <inheritdoc cref="IDictionary{TKey,TValue}" />
    public bool TryGetValue(TKey key, out TValue value)
    {
        CheckAlive();
        int slot = GetSlot(key, _header->Capacity, _header->Bitset, (TKey*)_header->Keys);
        if (IsSlotOccupied(_header->Bitset, slot))
        {
            value = ((TValue*)_header->Values)[slot];
            return true;
        }
        value = default;
        return false;
    }

    /// <inheritdoc />
    public void Clear()
    {
        CheckAlive();
        int bitsetWords = (_header->Capacity + 63) / 64;
        new Span<ulong>(_header->Bitset, bitsetWords).Clear();
        _header->Count = 0;
    }

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
        if (array.Length - arrayIndex < Count) throw new ArgumentException("Destination array is too small.");

        CheckAlive();
        int i = 0;
        foreach (var entry in this)
        {
            array[arrayIndex + i++] = entry;
        }
    }

    bool ICollection<KeyValuePair<TKey, TValue>>.Remove(KeyValuePair<TKey, TValue> item) => throw new NotSupportedException();

    /// <inheritdoc />
    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() => new Enumerator(this);

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>
    /// Enumerator for <see cref="ArenaDictionary{TKey, TValue}"/>.
    /// </summary>
    public struct Enumerator : IEnumerator<KeyValuePair<TKey, TValue>>
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

        /// <inheritdoc />
        public bool MoveNext()
        {
            _dict.CheckAlive();
            var header = _dict._header;
            if (header == null) return false;

            int cap = header->Capacity;
            ulong* bitset = header->Bitset;
            TKey* keys = (TKey*)header->Keys;
            TValue* values = (TValue*)header->Values;

            while (++_index < cap)
            {
                if (IsSlotOccupied(bitset, _index))
                {
                    _current = new KeyValuePair<TKey, TValue>(keys[_index], values[_index]);
                    return true;
                }
            }
            return false;
        }

        /// <inheritdoc />
        public KeyValuePair<TKey, TValue> Current => _current;
        object IEnumerator.Current => _current;

        /// <inheritdoc />
        public void Dispose() { }

        /// <inheritdoc />
        public void Reset() => _index = -1;
    }

    private readonly struct KeyCollection(ArenaDictionary<TKey, TValue> dict) : ICollection<TKey>
    {
        private readonly ArenaDictionary<TKey, TValue> _dict = dict;
        public int Count => _dict.Count;
        public bool IsReadOnly => true;
        public void Add(TKey item) => throw new NotSupportedException();
        public void Clear() => throw new NotSupportedException();
        public bool Contains(TKey item) => _dict.ContainsKey(item);
        public void CopyTo(TKey[] array, int arrayIndex)
        {
            int i = 0;
            foreach (var key in this) array[arrayIndex + i++] = key;
        }
        public bool Remove(TKey item) => throw new NotSupportedException();
        public IEnumerator<TKey> GetEnumerator()
        {
            foreach (var entry in _dict) yield return entry.Key;
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private readonly struct ValueCollection(ArenaDictionary<TKey, TValue> dict) : ICollection<TValue>
    {
        public int Count => dict.Count;
        public bool IsReadOnly => true;
        public void Add(TValue item) => throw new NotSupportedException();
        public void Clear() => throw new NotSupportedException();
        public bool Contains(TValue item)
        {
            foreach (var v in this) if (EqualityComparer<TValue>.Default.Equals(v, item)) return true;
            return false;
        }
        public void CopyTo(TValue[] array, int arrayIndex)
        {
            int i = 0;
            foreach (var val in this) array[arrayIndex + i++] = val;
        }
        public bool Remove(TValue item) => throw new NotSupportedException();
        public IEnumerator<TValue> GetEnumerator()
        {
            foreach (var entry in dict) yield return entry.Value;
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
