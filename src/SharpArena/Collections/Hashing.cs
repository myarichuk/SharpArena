using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.IO.Hashing;

namespace SharpArena.Collections;

internal static unsafe class Hashing
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint Hash<T>(T value) where T : unmanaged
    {
        if (typeof(T) == typeof(ArenaUtf16String))
        {
            return (uint)Unsafe.As<T, ArenaUtf16String>(ref value).GetHashCode();
        }
        if (typeof(T) == typeof(ArenaUtf8String))
        {
            return (uint)Unsafe.As<T, ArenaUtf8String>(ref value).GetHashCode();
        }

        return (uint)EqualityComparer<T>.Default.GetHashCode(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint HashString(ReadOnlySpan<char> value)
    {
        return (uint)XxHash3.HashToUInt64(MemoryMarshal.AsBytes(value));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint HashUtf8(ReadOnlySpan<byte> value)
    {
        return (uint)XxHash3.HashToUInt64(value);
    }
}
