using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.IO.Hashing;

namespace SharpArena.Collections;

internal static unsafe class Hashing
{
    // Bucket index is `hash & mask` against a power-of-two table, which only ever looks at the
    // hash's low bits. For a raw `int.GetHashCode()` (the value itself, e.g. sequential or
    // strided keys like i << 16), those low bits can be constant or near-constant across the
    // whole key set, collapsing probing to O(n) per lookup. Fibonacci hashing (multiply by the
    // golden-ratio-derived odd constant and keep the high bits) spreads entropy from the high
    // bits of the input down into the low bits used for indexing, independent of what pattern
    // the raw hash codes follow.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint Mix(uint h) => (uint)((h * 0x9E3779B97F4A7C15UL) >> 32);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint Hash<T>(T value) where T : unmanaged
    {
        if (typeof(T) == typeof(ArenaUtf16String))
        {
            return Mix((uint)Unsafe.As<T, ArenaUtf16String>(ref value).GetHashCode());
        }
        if (typeof(T) == typeof(ArenaUtf8String))
        {
            return Mix((uint)Unsafe.As<T, ArenaUtf8String>(ref value).GetHashCode());
        }

        return Mix((uint)EqualityComparer<T>.Default.GetHashCode(value));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint HashString(ReadOnlySpan<char> value) =>
        Mix((uint)XxHash3.HashToUInt64(MemoryMarshal.AsBytes(value)));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint HashUtf8(ReadOnlySpan<byte> value) =>
        Mix((uint)XxHash3.HashToUInt64(value));
}
