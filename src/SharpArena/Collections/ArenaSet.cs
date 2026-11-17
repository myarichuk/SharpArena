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
    public int GrowThreshold; // Count at/above which the table grows before the next insert; always < Capacity
    public int* Buckets; // 1-based indices (0 = empty)
    public uint* Hashes; // Contiguous cached (mixed) hash per entry, parallel to Entries
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

    // A table with fewer than 4 buckets fills up completely under LoadFactor rounding, which
    // turns a lookup miss in FindBucket into an infinite loop since it never finds an empty
    // bucket to stop at.
    private const int MinCapacity = 4;

    // Bounds the `capacity <<= 1` doubling loop below so it can never overflow into 0 or negative
    // and spin forever when a caller passes an enormous initialCapacity.
    private const int MaxCapacity = 1 << 30;

    /// <summary>
    /// Initializes a new instance of the <see cref="ArenaSet{T}"/> struct.
    /// </summary>
    /// <param name="arena">The allocator providing unmanaged storage.</param>
    /// <param name="initialCapacity">The initial capacity of the set. Will be rounded up to the nearest power of two.</param>
    public ArenaSet(ArenaAllocator arena, int initialCapacity = 16)
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

        var capacity = MinCapacity;
        while (capacity < initialCapacity) capacity <<= 1;

        _header = (ArenaSetHeader*)arena.Alloc((nuint)sizeof(ArenaSetHeader), align: 8);
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
    /// cached hashes (no element is re-hashed).
    /// </summary>
    private void Resize(int newCap, int copyCount)
    {
        var newBuckets = (int*)_arena.Alloc((nuint)newCap * sizeof(int), align: 16);
        var newHashes = (uint*)_arena.Alloc((nuint)newCap * sizeof(uint), align: 16);
        var newEntries = (T*)_arena.Alloc((nuint)newCap * (nuint)sizeof(T), align: (nuint)UnsafeHelpers.AlignOf<T>());

        new Span<int>(newBuckets, newCap).Clear();

        if (copyCount > 0)
        {
            Buffer.MemoryCopy(_header->Hashes, newHashes, (nuint)newCap * sizeof(uint), (nuint)copyCount * sizeof(uint));
            Buffer.MemoryCopy(_header->Entries, newEntries, (nuint)newCap * (nuint)sizeof(T), (nuint)copyCount * (nuint)sizeof(T));
            OpenAddressing.Rebuild(newBuckets, newHashes, copyCount, (uint)newCap - 1);
        }

        _header->Capacity = newCap;
        _header->GrowThreshold = ComputeGrowThreshold(newCap);
        _header->Buckets = newBuckets;
        _header->Hashes = newHashes;
        _header->Entries = newEntries;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private readonly void CheckAlive() =>
        UnsafeHelpers.CheckAliveThrowIfNot(_arena, _generation, nameof(ArenaSet<>));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private readonly int FindBucket(in T item, uint hash) =>
        OpenAddressing.FindSlot(_header->Buckets, _header->Hashes, (T*)_header->Entries, (uint)_header->Capacity - 1, item, hash);

    /// <summary>
    /// Gets the number of elements contained in the <see cref="ArenaSet{T}"/>.
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
    /// Gets a value indicating whether the <see cref="ArenaSet{T}"/> is read-only.
    /// </summary>
    public readonly bool IsReadOnly => false;

    /// <summary>
    /// Adds an element to the current set and returns a value to indicate if the element was successfully added.
    /// </summary>
    /// <param name="item">The element to add to the set.</param>
    /// <returns><see langword="true"/> if the element is added to the set; <see langword="false"/> if the element is already in the set.</returns>
    public bool Add(T item)
    {
        CheckAlive();

        var hash = Hashing.Hash(item);
        var bucketIdx = FindBucket(item, hash);
        if (_header->Buckets[bucketIdx] != 0)
        {
            return false;
        }

        if (_header->Count >= _header->GrowThreshold)
        {
            Grow();
            bucketIdx = FindBucket(item, hash);
        }

        var newEntryIdx = _header->Count++;
        _header->Buckets[bucketIdx] = newEntryIdx + 1;
        _header->Hashes[newEntryIdx] = hash;
        ((T*)_header->Entries)[newEntryIdx] = item;
        return true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void Grow()
    {
        var oldCap = _header->Capacity;
        if (oldCap >= MaxCapacity)
        {
            throw new InvalidOperationException("ArenaSet capacity overflow.");
        }

        Resize(oldCap * 2, _header->Count);
    }

    /// <summary>
    /// Removes all elements from the <see cref="ArenaSet{T}"/>.
    /// </summary>
    public void Clear()
    {
        CheckAlive();
        new Span<int>(_header->Buckets, _header->Capacity).Clear();
        _header->Count = 0;
    }

    /// <summary>
    /// Determines whether the <see cref="ArenaSet{T}"/> contains a specific value.
    /// </summary>
    /// <param name="item">The object to locate in the <see cref="ArenaSet{T}"/>.</param>
    /// <returns><see langword="true"/> if <paramref name="item"/> is found in the <see cref="ArenaSet{T}"/>; otherwise, <see langword="false"/>.</returns>
    public readonly bool Contains(T item)
    {
        CheckAlive();
        return _header->Buckets[FindBucket(item, Hashing.Hash(item))] != 0;
    }

    /// <summary>
    /// Specialized Contains for ArenaUtf16String using ReadOnlySpan{char} to avoid allocations.
    /// </summary>
    /// <param name="item">The object to locate in the <see cref="ArenaSet{T}"/>.</param>
    /// <returns><see langword="true"/> if <paramref name="item"/> is found in the <see cref="ArenaSet{T}"/>; otherwise, <see langword="false"/>.</returns>
    public readonly bool Contains(ReadOnlySpan<char> item)
    {
        CheckAlive();
        return OpenAddressing.FindEntry(_header->Buckets, _header->Hashes, (T*)_header->Entries, (uint)_header->Capacity - 1, item) >= 0;
    }

    /// <summary>
    /// Specialized Contains for ArenaUtf8String using ReadOnlySpan{byte} to avoid allocations.
    /// </summary>
    /// <param name="item">The object to locate in the <see cref="ArenaSet{T}"/>.</param>
    /// <returns><see langword="true"/> if <paramref name="item"/> is found in the <see cref="ArenaSet{T}"/>; otherwise, <see langword="false"/>.</returns>
    public readonly bool Contains(ReadOnlySpan<byte> item)
    {
        CheckAlive();
        return OpenAddressing.FindEntry(_header->Buckets, _header->Hashes, (T*)_header->Entries, (uint)_header->Capacity - 1, item) >= 0;
    }

    /// <summary>
    /// Copies the elements of the <see cref="ArenaSet{T}"/> to an array, starting at a particular array index.
    /// </summary>
    /// <param name="array">The one-dimensional array that is the destination of the elements copied from <see cref="ArenaSet{T}"/>. The array must have zero-based indexing.</param>
    /// <param name="arrayIndex">The zero-based index in <paramref name="array"/> at which copying begins.</param>
    /// <exception cref="ArgumentNullException"><paramref name="array"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="arrayIndex"/> is less than 0.</exception>
    /// <exception cref="ArgumentException">The number of elements in the source <see cref="ArenaSet{T}"/> is greater than the available space from <paramref name="arrayIndex"/> to the end of the destination <paramref name="array"/>.</exception>
    public void CopyTo(T[] array, int arrayIndex)
    {
        if (array == null)
        {
            throw new ArgumentNullException(nameof(array));
        }

        if (arrayIndex < 0 || arrayIndex > array.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(arrayIndex));
        }

        if (array.Length - arrayIndex < Count)
        {
            throw new ArgumentException("Destination array is too small.");
        }

        CheckAlive();
        var count = _header->Count;
        var entries = (T*)_header->Entries;

        for (var i = 0; i < count; i++)
        {
            array[arrayIndex + i] = entries[i];
        }
    }

    /// <summary>
    /// Shrinks the logical capacity of the hash table down to fit its current contents and
    /// rebuilds the bucket index, which improves probe locality.
    /// </summary>
    /// <remarks>
    /// This does not reduce the arena's actual memory footprint: the arena is a bump allocator
    /// with no general free(), so the previous buckets/entries buffers stay allocated (and
    /// unreachable) in the segment they came from. This can make <see cref="ArenaAllocator.AllocatedBytes"/>
    /// go up rather than down. Call this to speed up future lookups after a burst of inserts
    /// followed by removals-via-<see cref="Clear"/>, not to reclaim memory.
    /// </remarks>
    public void TrimExcess()
    {
        CheckAlive();
        var count = _header->Count;
        var newCap = MinCapacity;
        while (ComputeGrowThreshold(newCap) < count) newCap <<= 1;
        if (newCap >= _header->Capacity)
        {
            return;
        }

        Resize(newCap, count);
    }

    /// <summary>
    /// Removes the first occurrence of a specific object from the <see cref="ArenaSet{T}"/>. Not supported.
    /// </summary>
    public bool Remove(T item) => throw new NotSupportedException("ArenaSet does not support removal.");
    void ICollection<T>.Add(T item) => Add(item);

    /// <summary>
    /// Returns an enumerator that iterates through the <see cref="ArenaSet{T}"/>.
    /// </summary>
    /// <returns>A struct enumerator for the <see cref="ArenaSet{T}"/>. A <c>foreach</c> loop binds to this
    /// method directly and never boxes, unlike the <see cref="IEnumerable{T}"/> interface below.</returns>
    public readonly Enumerator GetEnumerator() => new Enumerator(this);
    readonly IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();
    readonly IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>
    /// Enumerates the elements of an <see cref="ArenaSet{T}"/>.
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

        /// <summary>
        /// Advances the enumerator to the next element of the <see cref="ArenaSet{T}"/>.
        /// </summary>
        /// <returns><see langword="true"/> if the enumerator was successfully advanced to the next element; <see langword="false"/> if the enumerator has passed the end of the collection.</returns>
        public bool MoveNext()
        {
            _set.CheckAlive();
            var header = _set._header;
            if (header == null)
            {
                return false;
            }

            if (++_index < header->Count)
            {
                _current = ((T*)header->Entries)[_index];
                return true;
            }
            return false;
        }

        /// <summary>
        /// Gets the element in the collection at the current position of the enumerator.
        /// </summary>
        public readonly T Current => _current;

        /// <summary>
        /// Gets the element in the collection at the current position of the enumerator.
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

    #region ISet Implementation
    /// <summary>Not supported.</summary>
    public void ExceptWith(IEnumerable<T> other) => throw new NotSupportedException();
    /// <summary>Not supported.</summary>
    public void IntersectWith(IEnumerable<T> other) => throw new NotSupportedException();

    /// <summary>
    /// Determines whether the current set is a proper (strict) subset of a specified collection.
    /// </summary>
    public readonly bool IsProperSubsetOf(IEnumerable<T> other)
    {
        if (other == null)
        {
            throw new ArgumentNullException(nameof(other));
        }

        CheckAlive();
        var matchCount = 0;
        var otherSet = new HashSet<T>(other); 
        foreach (var item in this)
        {
            if (!otherSet.Contains(item))
            {
                return false;
            }

            matchCount++;
        }
        return otherSet.Count > matchCount;
    }

    /// <summary>
    /// Determines whether the current set is a proper (strict) superset of a specified collection.
    /// </summary>
    public readonly bool IsProperSupersetOf(IEnumerable<T> other)
    {
        if (other == null)
        {
            throw new ArgumentNullException(nameof(other));
        }

        CheckAlive();

        // Duplicates in `other` must not be counted more than once — {1,2}.IsProperSupersetOf([1,1])
        // is true (distinct count 1 < 2), which a raw per-item counter would get wrong.
        int distinctCount;
        if (other is ArenaSet<T> arenaSet)
        {
            foreach (var item in arenaSet)
            {
                if (!Contains(item)) return false;
            }
            distinctCount = arenaSet.Count;
        }
        else
        {
            var otherSet = new HashSet<T>(other);
            foreach (var item in otherSet)
            {
                if (!Contains(item)) return false;
            }
            distinctCount = otherSet.Count;
        }

        return Count > distinctCount;
    }

    /// <summary>
    /// Determines whether a set is a subset of a specified collection.
    /// </summary>
    public readonly bool IsSubsetOf(IEnumerable<T> other)
    {
        if (other == null)
        {
            throw new ArgumentNullException(nameof(other));
        }

        CheckAlive();
        if (Count == 0)
        {
            return true;
        }

        var otherSet = new HashSet<T>(other);
        foreach (var item in this)
        {
            if (!otherSet.Contains(item))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Determines whether the current set is a superset of a specified collection.
    /// </summary>
    public readonly bool IsSupersetOf(IEnumerable<T> other)
    {
        if (other == null)
        {
            throw new ArgumentNullException(nameof(other));
        }

        CheckAlive();
        foreach (var item in other)
        {
            if (!Contains(item))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Determines whether the current set overlaps with the specified collection.
    /// </summary>
    public readonly bool Overlaps(IEnumerable<T> other)
    {
        if (other == null)
        {
            throw new ArgumentNullException(nameof(other));
        }

        CheckAlive();
        foreach (var item in other)
        {
            if (Contains(item))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Determines whether the current set and the specified collection contain the same elements.
    /// </summary>
    public readonly bool SetEquals(IEnumerable<T> other)
    {
        if (other == null)
        {
            throw new ArgumentNullException(nameof(other));
        }

        CheckAlive();

        // Duplicates in `other` must not be counted more than once — {1}.SetEquals([1,1]) is true,
        // since as sets {1} and {1,1} are the same, which a raw per-item counter would get wrong.
        int distinctCount;
        if (other is ArenaSet<T> arenaSet)
        {
            foreach (var item in arenaSet)
            {
                if (!Contains(item)) return false;
            }
            distinctCount = arenaSet.Count;
        }
        else
        {
            var otherSet = new HashSet<T>(other);
            foreach (var item in otherSet)
            {
                if (!Contains(item)) return false;
            }
            distinctCount = otherSet.Count;
        }

        return Count == distinctCount;
    }

    /// <summary>Not supported.</summary>
    public void SymmetricExceptWith(IEnumerable<T> other) => throw new NotSupportedException();

    /// <summary>
    /// Modifies the current set so that it contains all elements that are present in the current set, in the specified collection, or in both.
    /// </summary>
    public void UnionWith(IEnumerable<T> other)
    {
        if (other == null)
        {
            throw new ArgumentNullException(nameof(other));
        }

        CheckAlive();
        foreach (var item in other) Add(item);
    }
    #endregion
}
