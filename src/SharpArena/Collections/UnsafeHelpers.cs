using System.Runtime.CompilerServices;
using SharpArena.Allocators;

namespace SharpArena.Collections;

/// <summary>
/// Provides helper methods for unsafe and memory alignment operations.
/// </summary>
public class UnsafeHelpers
{
    /// <summary>
    /// Computes the proper memory alignment requirement for type T.
    /// </summary>
    /// <typeparam name="T">The unmanaged type to determine alignment for.</typeparam>
    /// <returns>The alignment size in bytes, rounded up to a power of two.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int AlignOf<T>() where T : unmanaged
    {
        var size = Unsafe.SizeOf<T>();

        // assume at least pointer-size alignment (worst case bit over-align)
        var required = size < IntPtr.Size ? IntPtr.Size : size;
#if NETCOREAPP3_0_OR_GREATER || NET
        return (int)System.Numerics.BitOperations.RoundUpToPowerOf2((uint)required);
#else
        return RoundUpToPowerOfTwo(required);
#endif
    }

#if !(NETCOREAPP3_0_OR_GREATER || NET)
    private static int RoundUpToPowerOfTwo(int value)
    {
        if (value <= 0)
        {
            return 1;
        }

        value--;
        value |= value >> 1;
        value |= value >> 2;
        value |= value >> 4;
        value |= value >> 8;
        value |= value >> 16;
        value++;
        return value;
    }
#endif
}
