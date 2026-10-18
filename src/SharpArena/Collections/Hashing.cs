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

    // The span-based hashes below must agree with ArenaUtf16String.GetHashCode /
    // ArenaUtf8String.GetHashCode (routed through Hash<T> above), because the same bucket
    // table is probed by both `TKey`-based and span-based lookups. Both GetHashCode
    // implementations return 0 for a default/empty string, whereas XxHash3 of an empty input is
    // a fixed non-zero value (0x2D06800538D394C2). Without this special case an empty key stored
    // via `TKey` could never be found by `ReadOnlySpan<char>.Empty` / `ReadOnlySpan<byte>.Empty`.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint HashString(ReadOnlySpan<char> value) =>
        value.IsEmpty ? Mix(0) : Mix((uint)XxHash3.HashToUInt64(MemoryMarshal.AsBytes(value)));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint HashUtf8(ReadOnlySpan<byte> value) =>
        value.IsEmpty ? Mix(0) : Mix((uint)XxHash3.HashToUInt64(value));
}
