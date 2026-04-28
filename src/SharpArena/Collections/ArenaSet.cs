using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SharpArena.Allocators;

namespace SharpArena.Collections;

/// <summary>
/// Metadata describing the shared state of an <see cref="ArenaSet{T}"/>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ArenaSetHeader
{
    public int Count;
    public int Capacity; // Must be power of 2
    public void* Entries;
    public ulong* Bitset;
}

/// <summary>
/// A high-performance, arena-backed, add-only hash set for unmanaged types.
/// Implements <see cref="ISet{T}"/> but does not support removals.
/// </summary>
/// <typeparam name="T">The unmanaged type to store.</typeparam>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct ArenaSet<T> : ISet<T>, IReadOnlyCollection<T>
    where T : unmanaged, IEquatable<T>
{
    private readonly ArenaAllocator _arena;
    private readonly int _generation;
    private ArenaSetHeader* _header;

    private const float LoadFactor = 0.7f;

    /// <summary>
    /// Initializes a new instance of the <see cref="ArenaSet{T}"/> struct.
    /// </summary>
    /// <param name="arena">The allocator to use.</param>
    /// <param name="initialCapacity">The initial capacity (will be rounded up to the next power of 2).</param>
    public ArenaSet(ArenaAllocator arena, int initialCapacity = 16)
    {
        _arena = arena ?? throw new ArgumentNullException(nameof(arena));
        _generation = arena.CurrentGeneration;

        int capacity = 1;
        while (capacity < initialCapacity) capacity <<= 1;

        _header = (ArenaSetHeader*)arena.Alloc((nuint)sizeof(ArenaSetHeader), align: (nuint)IntPtr.Size);
        _header->Count = 0;
        _header->Capacity = capacity;

        AllocateTable(capacity);
    }

    private void AllocateTable(int capacity)
    {
        // Entries + Bitset (1 bit per entry, aligned to 8 bytes)
        int bitsetWords = (capacity + 63) / 64;
        nuint entriesSize = (nuint)capacity * (nuint)sizeof(T);
        nuint bitsetSize = (nuint)bitsetWords * (nuint)sizeof(ulong);

        // Allocate together to minimize arena segments/fragmentation
        void* block = _arena.Alloc(entriesSize + bitsetSize, align: (nuint)UnsafeHelpers.AlignOf<T>());
        
        _header->Entries = block;
        _header->Bitset = (ulong*)((byte*)block + entriesSize);

        // Zero the bitset
        new Span<ulong>(_header->Bitset, bitsetWords).Clear();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void CheckAlive()
    {
        UnsafeHelpers.CheckAliveThrowIfNot(_arena, _generation, nameof(ArenaSet<T>));
    }

    private int GetSlot(T item, int capacity, ulong* bitset, T* entries)
    {
        uint hash = (uint)item.GetHashCode();
        uint mask = (uint)capacity - 1;
        uint index = hash & mask;

        while (true)
        {
            if (!IsSlotOccupied(bitset, (int)index))
            {
                return (int)index;
            }

            if (entries[index].Equals(item))
            {
                return (int)index;
            }

            index = (index + 1) & mask;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsSlotOccupied(ulong* bitset, int index)
    {
        return (bitset[index >> 6] & (1UL << (index & 63))) != 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void SetSlotOccupied(ulong* bitset, int index)
    {
        bitset[index >> 6] |= (1UL << (index & 63));
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
    public bool Add(T item)
    {
        CheckAlive();

        if (_header->Count >= _header->Capacity * LoadFactor)
        {
            Grow();
        }

        int slot = GetSlot(item, _header->Capacity, _header->Bitset, (T*)_header->Entries);
        if (IsSlotOccupied(_header->Bitset, slot))
        {
            return false;
        }

        SetSlotOccupied(_header->Bitset, slot);
        ((T*)_header->Entries)[slot] = item;
        _header->Count++;
        return true;
    }

    private void Grow()
    {
        int oldCap = _header->Capacity;
        int newCap = oldCap * 2;
        void* oldEntries = _header->Entries;
        ulong* oldBitset = _header->Bitset;

        AllocateTable(newCap);
        _header->Capacity = newCap;
        _header->Count = 0; // Will be incremented during re-add

        for (int i = 0; i < oldCap; i++)
        {
            if ((oldBitset[i >> 6] & (1UL << (i & 63))) != 0)
            {
                AddInternal(((T*)oldEntries)[i]);
            }
        }
    }

    // Faster add for rehashing (skips growth check and alive check)
    private void AddInternal(T item)
    {
        int slot = GetSlot(item, _header->Capacity, _header->Bitset, (T*)_header->Entries);
        SetSlotOccupied(_header->Bitset, slot);
        ((T*)_header->Entries)[slot] = item;
        _header->Count++;
    }

    /// <inheritdoc />
    public void Clear()
    {
        CheckAlive();
        int bitsetWords = (_header->Capacity + 63) / 64;
        new Span<ulong>(_header->Bitset, bitsetWords).Clear();
        _header->Count = 0;
    }

    /// <inheritdoc />
    public bool Contains(T item)
    {
        CheckAlive();
        int slot = GetSlot(item, _header->Capacity, _header->Bitset, (T*)_header->Entries);
        return IsSlotOccupied(_header->Bitset, slot);
    }

    /// <inheritdoc />
    public void CopyTo(T[] array, int arrayIndex)
    {
        if (array == null) throw new ArgumentNullException(nameof(array));
        if (arrayIndex < 0 || arrayIndex > array.Length) throw new ArgumentOutOfRangeException(nameof(arrayIndex));
        if (array.Length - arrayIndex < Count) throw new ArgumentException("Destination array is too small.");

        CheckAlive();
        int cap = _header->Capacity;
        ulong* bitset = _header->Bitset;
        T* entries = (T*)_header->Entries;

        int j = arrayIndex;
        for (int i = 0; i < cap; i++)
        {
            if (IsSlotOccupied(bitset, i))
            {
                array[j++] = entries[i];
            }
        }
    }

    /// <inheritdoc />
    public bool Remove(T item) => throw new NotSupportedException("ArenaSet does not support removal.");

    /// <inheritdoc />
    void ICollection<T>.Add(T item) => Add(item);

    /// <inheritdoc />
    public IEnumerator<T> GetEnumerator() => new Enumerator(this);

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>
    /// Enumerator for <see cref="ArenaSet{T}"/>.
    /// </summary>
    public struct Enumerator : IEnumerator<T>
    {
        private readonly ArenaSet<T> _set;
        private int _index;
        private T _current;

        internal Enumerator(ArenaSet<T> set)
        {
            _set = set;
            _index = -1;
            _current = default;
        }

        /// <inheritdoc />
        public bool MoveNext()
        {
            _set.CheckAlive();
            var header = _set._header;
            if (header == null) return false;

            int cap = header->Capacity;
            ulong* bitset = header->Bitset;
            T* entries = (T*)header->Entries;

            while (++_index < cap)
            {
                if ((bitset[_index >> 6] & (1UL << (_index & 63))) != 0)
                {
                    _current = entries[_index];
                    return true;
                }
            }
            return false;
        }

        /// <inheritdoc />
        public T Current => _current;

        object IEnumerator.Current => _current;

        /// <inheritdoc />
        public void Dispose() { }

        /// <inheritdoc />
        public void Reset() => _index = -1;
    }

    #region ISet Implementation

    /// <inheritdoc />
    public void ExceptWith(IEnumerable<T> other) => throw new NotSupportedException("ArenaSet does not support removal.");

    /// <inheritdoc />
    public void IntersectWith(IEnumerable<T> other)
    {
        // This usually involves removal. For add-only, we can't truly Intersect in-place.
        throw new NotSupportedException("ArenaSet does not support in-place intersection (requires removal).");
    }

    /// <inheritdoc />
    public bool IsProperSubsetOf(IEnumerable<T> other)
    {
        if (other == null) throw new ArgumentNullException(nameof(other));
        CheckAlive();
        if (Count == 0)
        {
            // Proper subset of any non-empty set
            foreach (var _ in other) return true;
            return false;
        }

        int matchCount = 0;
        var otherSet = new HashSet<T>(other); 
        
        foreach (var item in this)
        {
            if (!otherSet.Contains(item)) return false;
            matchCount++;
        }
        
        return otherSet.Count > matchCount;
    }

    /// <inheritdoc />
    public bool IsProperSupersetOf(IEnumerable<T> other)
    {
        if (other == null) throw new ArgumentNullException(nameof(other));
        CheckAlive();
        
        int otherCount = 0;
        foreach (var item in other)
        {
            if (!Contains(item)) return false;
            otherCount++;
        }
        return Count > otherCount;
    }

    /// <inheritdoc />
    public bool IsSubsetOf(IEnumerable<T> other)
    {
        if (other == null) throw new ArgumentNullException(nameof(other));
        CheckAlive();
        if (Count == 0) return true;

        var otherSet = new HashSet<T>(other);
        foreach (var item in this)
        {
            if (!otherSet.Contains(item)) return false;
        }
        return true;
    }

    /// <inheritdoc />
    public bool IsSupersetOf(IEnumerable<T> other)
    {
        if (other == null) throw new ArgumentNullException(nameof(other));
        CheckAlive();
        foreach (var item in other)
        {
            if (!Contains(item)) return false;
        }
        return true;
    }

    /// <inheritdoc />
    public bool Overlaps(IEnumerable<T> other)
    {
        if (other == null) throw new ArgumentNullException(nameof(other));
        CheckAlive();
        foreach (var item in other)
        {
            if (Contains(item)) return true;
        }
        return false;
    }

    /// <inheritdoc />
    public bool SetEquals(IEnumerable<T> other)
    {
        if (other == null) throw new ArgumentNullException(nameof(other));
        CheckAlive();
        
        int otherCount = 0;
        foreach (var item in other)
        {
            if (!Contains(item)) return false;
            otherCount++;
        }
        return Count == otherCount;
    }

    /// <inheritdoc />
    public void SymmetricExceptWith(IEnumerable<T> other) => throw new NotSupportedException("ArenaSet does not support removal.");

    /// <inheritdoc />
    public void UnionWith(IEnumerable<T> other)
    {
        if (other == null) throw new ArgumentNullException(nameof(other));
        CheckAlive();
        foreach (var item in other)
        {
            Add(item);
        }
    }

    #endregion
}
