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
    public int* Buckets; // 1-based indices (0 = empty)
    public void* Entries; // Contiguous T*
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
    public ArenaSet(ArenaAllocator arena, int initialCapacity = 16)
    {
        _arena = arena ?? throw new ArgumentNullException(nameof(arena));
        _generation = arena.CurrentGeneration;

        var capacity = 1;
        while (capacity < initialCapacity) capacity <<= 1;

        _header = (ArenaSetHeader*)arena.Alloc((nuint)sizeof(ArenaSetHeader), align: 8);
        _header->Count = 0;
        _header->Capacity = capacity;

        AllocateTable(capacity);
    }

    private void AllocateTable(int capacity)
    {
        var bucketsSize = (nuint)capacity * sizeof(int);
        var entriesSize = (nuint)capacity * (nuint)sizeof(T);

        // Align to 16 bytes for potential SIMD
        _header->Buckets = (int*)_arena.Alloc(bucketsSize, align: 16);
        _header->Entries = _arena.Alloc(entriesSize, align: (nuint)UnsafeHelpers.AlignOf<T>());
        
        new Span<int>(_header->Buckets, capacity).Clear();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private readonly void CheckAlive() => 
        UnsafeHelpers.CheckAliveThrowIfNot(_arena, _generation, nameof(ArenaSet<>));

    private int FindBucket(T item)
    {
        var capacity = _header->Capacity;
        var buckets = _header->Buckets;
        var entries = (T*)_header->Entries;
        var mask = (uint)capacity - 1;
        var hash = Hashing.Hash(item);
        var index = hash & mask;

        while (true)
        {
            var entryIdxPlusOne = buckets[index];
            if (entryIdxPlusOne == 0) return (int)index;

            if (entries[entryIdxPlusOne - 1].Equals(item)) return (int)index;

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

    public bool Add(T item)
    {
        CheckAlive();

        if (_header->Count >= _header->Capacity * LoadFactor)
        {
            Grow();
        }

        var bucketIdx = FindBucket(item);
        var entryIdxPlusOne = _header->Buckets[bucketIdx];
        
        if (entryIdxPlusOne != 0) return false;

        var newEntryIdx = _header->Count++;
        _header->Buckets[bucketIdx] = newEntryIdx + 1;
        ((T*)_header->Entries)[newEntryIdx] = item;
        return true;
    }

    private void Grow()
    {
        var oldCap = _header->Capacity;
        var newCap = oldCap * 2;
        var oldEntries = (T*)_header->Entries;
        var count = _header->Count;

        var newBucketsSize = (nuint)newCap * sizeof(int);
        var newEntriesSize = (nuint)newCap * (nuint)sizeof(T);

        var newBuckets = (int*)_arena.Alloc(newBucketsSize, align: 16);
        var newEntries = (T*)_arena.Alloc(newEntriesSize, align: (nuint)UnsafeHelpers.AlignOf<T>());
        
        new Span<int>(newBuckets, newCap).Clear();
        
        // Copy old entries to new entries (contiguous)
        Unsafe.CopyBlockUnaligned(newEntries, oldEntries, (uint)(count * sizeof(T)));

        _header->Capacity = newCap;
        _header->Buckets = newBuckets;
        _header->Entries = newEntries;

        // Re-index
        var mask = (uint)newCap - 1;
        for (int i = 0; i < count; i++)
        {
            var hash = Hashing.Hash(newEntries[i]);
            var idx = hash & mask;
            while (newBuckets[idx] != 0) idx = (idx + 1) & mask;
            newBuckets[idx] = i + 1;
        }
    }

    public void Clear()
    {
        CheckAlive();
        new Span<int>(_header->Buckets, _header->Capacity).Clear();
        _header->Count = 0;
    }

    public bool Contains(T item)
    {
        CheckAlive();
        var bucketIdx = FindBucket(item);
        return _header->Buckets[bucketIdx] != 0;
    }

    /// <summary>
    /// Specialized Contains for ArenaString using ReadOnlySpan{char} to avoid allocations.
    /// </summary>
    public bool Contains(ReadOnlySpan<char> item)
    {
        if (typeof(T) != typeof(ArenaString)) return false;
        CheckAlive();
        
        var capacity = _header->Capacity;
        var buckets = _header->Buckets;
        var entries = (ArenaString*)_header->Entries;
        var mask = (uint)capacity - 1;
        var hash = Hashing.HashString(item);
        var index = hash & mask;

        while (true)
        {
            var entryIdxPlusOne = buckets[index];
            if (entryIdxPlusOne == 0) return false;

            if (entries[entryIdxPlusOne - 1].Equals(item)) return true;

            index = (index + 1) & mask;
        }
    }

    /// <summary>
    /// Specialized Contains for ArenaUtf8String using ReadOnlySpan{byte} to avoid allocations.
    /// </summary>
    public bool Contains(ReadOnlySpan<byte> item)
    {
        if (typeof(T) != typeof(ArenaUtf8String)) return false;
        CheckAlive();

        var capacity = _header->Capacity;
        var buckets = _header->Buckets;
        var entries = (ArenaUtf8String*)_header->Entries;
        var mask = (uint)capacity - 1;
        var hash = Hashing.HashUtf8(item);
        var index = hash & mask;

        while (true)
        {
            var entryIdxPlusOne = buckets[index];
            if (entryIdxPlusOne == 0) return false;

            if (entries[entryIdxPlusOne - 1].Equals(item)) return true;

            index = (index + 1) & mask;
        }
    }

    public void CopyTo(T[] array, int arrayIndex)
    {
        if (array == null) throw new ArgumentNullException(nameof(array));
        if (arrayIndex < 0 || arrayIndex > array.Length) throw new ArgumentOutOfRangeException(nameof(arrayIndex));
        if (array.Length - arrayIndex < Count) throw new ArgumentException("Destination array is too small.");

        CheckAlive();
        var count = _header->Count;
        var entries = (T*)_header->Entries;

        for (var i = 0; i < count; i++)
        {
            array[arrayIndex + i] = entries[i];
        }
    }

    /// <summary>
    /// Reduces memory usage to the actual count and optimizes the hash table.
    /// </summary>
    public void TrimExcess()
    {
        CheckAlive();
        var count = _header->Count;
        if (count == 0)
        {
            _header->Capacity = 1;
            AllocateTable(1);
            return;
        }

        var newCap = 1;
        while (newCap < count / LoadFactor) newCap <<= 1;
        
        if (newCap >= _header->Capacity) return;

        var oldEntries = (T*)_header->Entries;
        var newBuckets = (int*)_arena.Alloc((nuint)newCap * sizeof(int), align: 16);
        var newEntries = (T*)_arena.Alloc((nuint)newCap * (nuint)sizeof(T), align: (nuint)UnsafeHelpers.AlignOf<T>());
        
        new Span<int>(newBuckets, newCap).Clear();
        Unsafe.CopyBlockUnaligned(newEntries, oldEntries, (uint)(count * sizeof(T)));

        _header->Capacity = newCap;
        _header->Buckets = newBuckets;
        _header->Entries = newEntries;

        var mask = (uint)newCap - 1;
        for (int i = 0; i < count; i++)
        {
            var hash = Hashing.Hash(newEntries[i]);
            var idx = hash & mask;
            while (newBuckets[idx] != 0) idx = (idx + 1) & mask;
            newBuckets[idx] = i + 1;
        }
    }

    public bool Remove(T item) => throw new NotSupportedException("ArenaSet does not support removal.");
    void ICollection<T>.Add(T item) => Add(item);
    public IEnumerator<T> GetEnumerator() => new Enumerator(this);
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public unsafe struct Enumerator : IEnumerator<T>
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

        public bool MoveNext()
        {
            _set.CheckAlive();
            var header = _set._header;
            if (header == null) return false;

            if (++_index < header->Count)
            {
                _current = ((T*)header->Entries)[_index];
                return true;
            }
            return false;
        }

        public T Current => _current;
        object IEnumerator.Current => _current;
        public void Dispose() { }
        public void Reset() => _index = -1;
    }

    #region ISet Implementation
    public void ExceptWith(IEnumerable<T> other) => throw new NotSupportedException();
    public void IntersectWith(IEnumerable<T> other) => throw new NotSupportedException();
    public bool IsProperSubsetOf(IEnumerable<T> other)
    {
        if (other == null) throw new ArgumentNullException(nameof(other));
        CheckAlive();
        var matchCount = 0;
        var otherSet = new HashSet<T>(other); 
        foreach (var item in this)
        {
            if (!otherSet.Contains(item)) return false;
            matchCount++;
        }
        return otherSet.Count > matchCount;
    }
    public bool IsProperSupersetOf(IEnumerable<T> other)
    {
        if (other == null) throw new ArgumentNullException(nameof(other));
        CheckAlive();
        var otherCount = 0;
        foreach (var item in other)
        {
            if (!Contains(item)) return false;
            otherCount++;
        }
        return Count > otherCount;
    }
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
    public bool SetEquals(IEnumerable<T> other)
    {
        if (other == null) throw new ArgumentNullException(nameof(other));
        CheckAlive();
        var otherCount = 0;
        foreach (var item in other)
        {
            if (!Contains(item)) return false;
            otherCount++;
        }
        return Count == otherCount;
    }
    public void SymmetricExceptWith(IEnumerable<T> other) => throw new NotSupportedException();
    public void UnionWith(IEnumerable<T> other)
    {
        if (other == null) throw new ArgumentNullException(nameof(other));
        CheckAlive();
        foreach (var item in other) Add(item);
    }
    #endregion
}
