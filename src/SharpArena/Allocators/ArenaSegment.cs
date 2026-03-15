using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace SharpArena.Allocators;

/// <summary>
/// Represents a single contiguous segment of memory in an arena.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct ArenaSegment
{
#if DEBUG
    /// <summary>
    /// A diagnostic value used to detect memory corruption at the head of the segment.
    /// </summary>
    public ulong HeadCanary;
    /// <summary>
    /// A diagnostic value used to detect memory corruption at the tail of the segment.
    /// </summary>
    public ulong TailCanary;

    internal const ulong Canary = 0xDEADBEEFCAFEBABEul;
#endif

    /// <summary>
    /// The current byte offset within the segment where the next allocation can occur.
    /// </summary>
    public nuint Offset;
    /// <summary>
    /// A pointer to the base address of the allocated block.
    /// </summary>
    public byte* Base;
    /// <summary>
    /// The total allocated size of this segment in bytes.
    /// </summary>
    public nuint Size;
    /// <summary>
    /// A pointer to the next segment in the arena, or null if this is the last segment.
    /// </summary>
    public ArenaSegment* Next;

    /// <summary>
    /// Attempts to allocate a block respecting the requested alignment within this segment.
    /// </summary>
    /// <param name="size">The number of bytes to reserve from the segment.</param>
    /// <param name="align">The required power-of-two alignment for the allocation.</param>
    /// <param name="ptr">When successful, receives the aligned address of the allocated block.</param>
    /// <returns><see langword="true"/> when the allocation fits within the remaining segment space.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryAlloc(nuint size, nuint align, out void* ptr)
    {
#if DEBUG
        Debug.Assert(HeadCanary == Canary, "Arena segment head canary corrupted");
        Debug.Assert(TailCanary == Canary, "Arena segment tail canary corrupted");
#endif
        if (align == 0)
        {
            align = (nuint)IntPtr.Size;
        }

        Debug.Assert((align & (align - 1)) == 0, "align must be a power of two");

        var current = (nuint)Base + Offset;

        // Align up the current offset to the requested power-of-two alignment
        // If the resulting aligned address wraps around (overflows), it will be less than the current address.
        var aligned = (current + (align - 1)) & ~(align - 1);

        if (aligned < current)
        {
            ptr = null;
            return false;
        }

        var newOffset = aligned - (nuint)Base;

        // overflow-safe bound check: newOffset <= Size - size
        if (size > Size || newOffset > Size - size)
        {
            ptr = null;
            return false;
        }

        ptr = (void*)aligned;
        Offset = newOffset + size;
        return true;
    }
}
