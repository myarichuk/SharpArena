using System;
using System.Runtime.CompilerServices;
using System.Threading;
namespace SharpArena.Allocators;

/// <summary>
/// A zero-allocation arena allocator for high-performance parsing.
/// </summary>
/// <remarks>
/// All memory allocated from this arena is invalid after <see cref="Reset"/> or <see cref="Dispose()"/> is called.
/// </remarks>
public unsafe class ArenaAllocator : IDisposable
{
    private static readonly nuint DefaultPageSize = (nuint)Environment.SystemPageSize;

    // Process-wide generation counter. Every arena draws its generation stamp from this
    // single counter (on construction, Reset, and Dispose) instead of starting at 0, so that
    // two different arenas can never share a generation number — which would otherwise let
    // IsAlive(otherArena) checks on strings/collections produce false positives across arenas.
    // This is the one Interlocked operation in the whole library: it only runs on the
    // construct/Reset/Dispose slow path, never on the hot Alloc path, so it does not
    // compromise the single-threaded, lock-free allocation fast path described in the README.
    private static int s_epoch;

    private ArenaSegment* _first;
    private ArenaSegment* _current;
    private readonly nuint _initialSize;
    private readonly nuint _maxSegmentSize;
    private readonly NativeAllocatorBackend _backend;
    private bool _disposed;
    private int _generation;

    private nuint _peakBytes;
    private nuint _allocatedBytes;

    /// <summary>
    /// Returns the current generation. Changes on every <see cref="Reset"/> and on <see cref="Dispose()"/>,
    /// so pointers, collections, and strings captured against a stale generation are detected as dead.
    /// </summary>
    public int CurrentGeneration => Volatile.Read(ref _generation);

    /// <summary>
    /// Gets the total number of bytes currently allocated from the arena.
    /// </summary>
    public nuint AllocatedBytes => _allocatedBytes;

    /// <summary>
    /// Gets the peak number of bytes allocated from the arena over its lifetime.
    /// </summary>
    public nuint PeakBytes
    {
        get
        {
            nuint currentAllocated = AllocatedBytes;
            return _peakBytes > currentAllocated ? _peakBytes : currentAllocated;
        }
    }
    
    /// <summary>
    /// Initializes a new instance of the <see cref="ArenaAllocator"/> class.
    /// </summary>
    /// <param name="initialSize">The initial size of the first segment.</param>
    /// <param name="maxSize">The maximum size a single segment can grow to.</param>
    /// <param name="backend">The backend to use for allocating native memory.</param>
    public ArenaAllocator(
        nuint initialSize = 64 * 1024,
        nuint maxSize = 256 * 1024 * 1024,
        NativeAllocatorBackend backend = NativeAllocatorBackend.PlatformInvoke)
    {
        if (maxSize < initialSize)
        {
            throw new ArgumentOutOfRangeException(nameof(maxSize), "maxSize must be greater than or equal to initialSize.");
        }

        _initialSize = initialSize == 0 ? 1 : initialSize;
        _maxSegmentSize = maxSize;
        _backend = backend;
        _generation = Interlocked.Increment(ref s_epoch);
        _first = _current = AllocateSegment(_initialSize);
    }

    /// <summary>
    /// Allocates memory from the arena.
    /// </summary>
    /// <param name="size">The size of the allocation in bytes.</param>
    /// <param name="align">The alignment of the allocation.</param>
    /// <returns>A pointer to the allocated memory.</returns>
    public void* Alloc(nuint size, nuint align = 8)
    {
        if (_disposed)
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

        // Alignment must be a power of two for the bit-mask math in ArenaSegment.TryAlloc to work.
        // Round whatever was requested up to the nearest valid power of two instead of silently
        // truncating it to a multiple (e.g. AlignUp(24, 8) == 24, which is not a power of two and
        // produces incorrectly-aligned pointers).
        align = RoundUpToPowerOfTwo(align < (nuint)IntPtr.Size ? (nuint)IntPtr.Size : align);
        var oldOffset = seg->Offset;
        if (seg->TryAlloc(size, align, out var ptr))
        {
            _allocatedBytes += (seg->Offset - oldOffset);
            return ptr;
        }

        while (true)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(ArenaAllocator));
            }

            if (seg->Next != null)
            {
                seg = seg->Next;
                _current = seg;
                oldOffset = seg->Offset;
                if (seg->TryAlloc(size, align, out ptr))
                {
                    _allocatedBytes += (seg->Offset - oldOffset);
                    return ptr;
                }
                continue;
            }

            // A fresh segment's usable region starts at `Base`, which itself sits past the
            // native allocation header and the ArenaSegment header. Alignment padding on top
            // of that can push the requested block past a segment sized for exactly `size`
            // bytes. Size the new segment for `size + align - 1` so the allocation is
            // guaranteed to fit regardless of where `Base` happens to land.
            nuint required;
            try
            {
                checked
                {
                    required = size + (align - 1);
                }
            }
            catch (OverflowException)
            {
                throw new OutOfMemoryException("Failed to allocate memory in arena; requested size and alignment overflow.");
            }

            var nextSize = NextSegmentSize(seg->Size, required);

            if (nextSize > _maxSegmentSize)
            {
                nextSize = AlignUp(required, DefaultPageSize); // fallback if request > maxSegmentSize
            }

            var newSeg = AllocateSegment(nextSize);
            seg->Next = newSeg;
            _current = newSeg;
            seg = newSeg;

            oldOffset = seg->Offset;
            if (!seg->TryAlloc(size, align, out ptr))
            {
                // The segment was sized specifically to satisfy this request; failing here means
                // the sizing math above is wrong, not that the arena is legitimately out of memory.
                throw new InvalidOperationException(
                    "Arena allocator invariant violated: a segment freshly allocated to satisfy this request failed to satisfy it.");
            }

            _allocatedBytes += (seg->Offset - oldOffset);
            return ptr;
        }
    }

    /// <summary>
    /// Attempts to grow or shrink an allocation in place, without copying, when <paramref name="ptr"/>
    /// is the most recent allocation made from the arena's current segment (a classic bump-allocator
    /// edge case: only the tail allocation can be resized without disturbing anything after it).
    /// </summary>
    /// <param name="ptr">Pointer previously returned by <see cref="Alloc"/>.</param>
    /// <param name="oldSize">The size that was originally requested for <paramref name="ptr"/>.</param>
    /// <param name="newSize">The desired new size.</param>
    /// <returns>
    /// <see langword="true"/> if the allocation was resized in place; <see langword="false"/> if
    /// <paramref name="ptr"/> is not the tail allocation of the current segment, or the new size
    /// does not fit — the caller must then fall back to a fresh <see cref="Alloc"/> and copy.
    /// </returns>
    public bool TryResizeInPlace(void* ptr, nuint oldSize, nuint newSize)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(ArenaAllocator));
        }

        if (ptr == null)
        {
            return false;
        }

        var seg = _current;
        if (seg == null || (byte*)ptr < seg->Base)
        {
            return false;
        }

        var allocOffset = (nuint)((byte*)ptr - seg->Base);
        if (allocOffset > seg->Size - oldSize || allocOffset + oldSize != seg->Offset)
        {
            // Not the tail allocation of the current segment.
            return false;
        }

        if (newSize > seg->Size - allocOffset)
        {
            return false;
        }

        seg->Offset = allocOffset + newSize;
        _allocatedBytes = newSize >= oldSize
            ? _allocatedBytes + (newSize - oldSize)
            : _allocatedBytes - (oldSize - newSize);
        return true;
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
        seg->HeadCanary = ArenaSegment.CANARY;
        seg->TailCanary = ArenaSegment.CANARY;
#endif
        return seg;
    }

    private nuint NextSegmentSize(nuint prev, nuint req)
    {
        var doubled = prev == 0
            ? _initialSize
            : prev > _maxSegmentSize / 2
                ? _maxSegmentSize
                : prev * 2;

        return req > doubled ? AlignUp(req, DefaultPageSize) : doubled;
    }

    /// <summary>
    /// Aligns a memory offset or address up to a specified alignment value.
    /// Ensures that an integer overflow exception is thrown if the alignment wraps around.
    /// </summary>
    private static nuint AlignUp(nuint value, nuint align)
    {
        align = RoundUpToPowerOfTwo(align);
        nuint result = (value + (align - 1)) & ~(align - 1);

        // If an overflow occurs when adding `align - 1`, the result wraps
        // and becomes strictly less than the original value.
        if (result < value)
        {
            throw new OverflowException("Alignment caused an integer overflow.");
        }
        return result;
    }

    /// <summary>
    /// Rounds up an unsigned integer to the nearest power of two using a bitwise shift approach.
    /// This guarantees O(1) alignment calculations for native integers.
    /// </summary>
    private static nuint RoundUpToPowerOfTwo(nuint x)
    {
        // 0 case
        if (x == 0)
        {
            return 1;
        }

        // Subtract 1 to prevent numbers that are already powers of two from doubling.
        x--;

        // Propagate the highest set bit downwards to fill all lower bits with 1s.
        x |= x >> 1;
        x |= x >> 2;
        x |= x >> 4;
        x |= x >> 8;
        x |= x >> 16;
#if TARGET_64BIT
        x |= x >> 32;
#endif

        // Overflow safety mechanism not explicitly required here,
        // because maximum value with all bits set (1) becomes 0 when incremented,
        // which triggers overflow checks up the stack in AlignUp.
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
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(ArenaAllocator));
        }

        var currentAllocated = AllocatedBytes;
        if (currentAllocated > _peakBytes)
        {
            _peakBytes = currentAllocated;
        }

        for (var seg = _first; seg != null; seg = seg->Next)
        {
            seg->Offset = 0;
        }

        _allocatedBytes = 0;
        _current = _first;
        _generation = Interlocked.Increment(ref s_epoch);
    }

    /// <summary>
    /// Disposes the arena, freeing all allocated unmanaged memory.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
    }

    private void Dispose(bool isDisposing)
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Bump the generation before freeing memory so any ArenaUtf16String/ArenaUtf8String
        // or collection still holding a pre-dispose generation snapshot fails CheckAliveThrowIfNot
        // instead of dereferencing freed memory.
        _generation = Interlocked.Increment(ref s_epoch);

        var head = _first;
        _first = _current = null;

        while (head != null)
        {
            var next = head->Next;
            if (isDisposing)
            {
                NativeAllocator.Free(head, _backend);
            }
            else
            {
                // On the finalizer thread, an exception here (e.g. a double-free or corruption
                // check tripping) would go unhandled and terminate the process. Best-effort only.
                try
                {
                    NativeAllocator.Free(head, _backend);
                }
                catch
                {
                    // Swallowed intentionally: see comment above.
                }
            }
            head = next;
        }

        if (isDisposing)
        {
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>
    /// Finalizes an instance of the <see cref="ArenaAllocator"/> class.
    /// </summary>
    ~ArenaAllocator()
    {
        Dispose(false);
    }
}
