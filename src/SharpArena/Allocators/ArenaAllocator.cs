using System;
using System.Runtime.CompilerServices;
using System.Threading;
namespace SharpArena.Allocators;

using SharpArena.Allocators;

/// <summary>
/// A zero-allocation arena allocator for high-performance parsing.
/// </summary>
/// <remarks>
/// All memory allocated from this arena is invalid after <see cref="Reset"/> or <see cref="Dispose"/> is called.
/// </remarks>
public unsafe class ArenaAllocator : IDisposable
{
    private static readonly nuint DefaultInitialSegmentSize = 64 * 1024;
    private static readonly nuint DefaultPageSize = (nuint)Environment.SystemPageSize;

    private ArenaSegment* _first;
    private ArenaSegment* _current;
    private readonly nuint _maxSegmentSize;
    private readonly NativeAllocatorBackend _backend;
    private bool _disposed;

    private readonly object _disposeLock = new();
    private int _activeAllocations;

    public ArenaAllocator(
        nuint initialSize = 64 * 1024,
        nuint maxSize = 256 * 1024 * 1024,
        NativeAllocatorBackend backend = NativeAllocatorBackend.PlatformInvoke)
    {
        _maxSegmentSize = maxSize;
        _backend = backend;
        _first = _current = AllocateSegment(initialSize);
    }

    /// <summary>
    /// Allocates memory from the arena.
    /// </summary>
    /// <param name="size">The size of the allocation in bytes.</param>
    /// <param name="align">The alignment of the allocation.</param>
    /// <returns>A pointer to the allocated memory.</returns>
    public void* Alloc(nuint size, nuint align = 8)
    {
        if (Volatile.Read(ref _disposed))
        {
            throw new ObjectDisposedException(nameof(ArenaAllocator));
        }

        Interlocked.Increment(ref _activeAllocations);

        try
        {
            if (Volatile.Read(ref _disposed))
            {
                throw new ObjectDisposedException(nameof(ArenaAllocator));
            }

            if (size == 0)
            {
                return null;
            }

            var seg = _current;
            if (seg is null)
            {
                throw new ObjectDisposedException(nameof(ArenaAllocator));
            }

            align = AlignUp(align, (nuint)IntPtr.Size);
            if (seg->TryAlloc(size, align, out var ptr))
            {
                return ptr;
            }

            while (true)
            {
                if (Volatile.Read(ref _disposed))
                {
                    throw new ObjectDisposedException(nameof(ArenaAllocator));
                }

                var nextSize = NextSegmentSize(seg->Size, size);

                if (nextSize < size)
                {
                    nextSize = AlignUp(size, DefaultPageSize);
                }

                if (nextSize > _maxSegmentSize)
                {
                    nextSize = AlignUp(size, DefaultPageSize); // fallback if request > maxSegmentSize
                }

                var newSeg = AllocateSegment(nextSize);
                seg->Next = newSeg;
                _current = newSeg;
                seg = newSeg;

                if (seg->TryAlloc(size, align, out ptr))
                {
                    return ptr;
                }

                if (nextSize == size)
                {
                    throw new OutOfMemoryException("Failed to allocate memory in arena; request too large.");
                }
            }
        }
        finally
        {
            Interlocked.Decrement(ref _activeAllocations);
        }
    }


    private ArenaSegment* AllocateSegment(nuint requestSize)
    {
        var prevSize = _current == null ? 0 : _current->Size;
        var segSize = NextSegmentSize(prevSize, requestSize);
        var total = (nuint)sizeof(ArenaSegment) + segSize;
        var mem = (byte*)NativeAllocator.Alloc(total, _backend);
        var seg = (ArenaSegment*)mem;
        seg->Next = null;
        seg->Offset = 0;
        seg->Size = segSize;
        seg->Base = mem + sizeof(ArenaSegment);
#if DEBUG
        seg->HeadCanary = ArenaSegment.Canary;
        seg->TailCanary = ArenaSegment.Canary;
#endif
        return seg;
    }

    private nuint NextSegmentSize(nuint prev, nuint req)
    {
        var doubled = prev == 0
            ? DefaultInitialSegmentSize
            : prev > _maxSegmentSize / 2
                ? _maxSegmentSize
                : prev * 2;

        return req > doubled ? AlignUp(req, DefaultPageSize) : doubled;
    }

    private static nuint AlignUp(nuint value, nuint align)
    {
        align = RoundUpToPowerOfTwo(align);
        return (value + (align - 1)) & ~(align - 1);
    }

    private static nuint RoundUpToPowerOfTwo(nuint x)
    {
        if (x == 0)
        {
            return 1;
        }

        x--;
        x |= x >> 1;
        x |= x >> 2;
        x |= x >> 4;
        x |= x >> 8;
        x |= x >> 16;
#if TARGET_64BIT
        x |= x >> 32;
#endif
        return x + 1;
    }

    /// <summary>
    /// Resets the arena, invalidating all prior allocations.
    /// </summary>
    /// <remarks>
    /// Any pointers returned by <see cref="Alloc"/> become invalid after this method returns.
    /// </remarks>
    public void Reset()
    {
        lock (_disposeLock)
        {
            for (var seg = _first; seg != null; seg = seg->Next)
            {
                seg->Offset = 0;
            }

            _current = _first;
        }
    }

    public void Dispose()
    {
        lock (_disposeLock)
        {
            Dispose(true);
        }
    }

    private void Dispose(bool isDisposing)
    {
        lock (_disposeLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            SpinWait spinner = default;
            while (Volatile.Read(ref _activeAllocations) != 0)
            {
                spinner.SpinOnce();
            }

            var head = _first;
            _first = _current = null;

            while (head != null)
            {
                var next = head->Next;
                NativeAllocator.Free(head, _backend);
                head = next;
            }

            if (isDisposing)
            {
                GC.SuppressFinalize(this);
            }
        }
    }

    /// <summary>
    /// Finalizes an instance of the <see cref="ArenaAllocator"/> class.
    /// </summary>
    ~ArenaAllocator()
    {
        lock (_disposeLock)
        {
            Dispose(false);
        }
    }
}
