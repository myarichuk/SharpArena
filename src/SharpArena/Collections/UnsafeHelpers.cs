using System;
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

    /// <summary>
    /// Checks if the provided arena is still valid and has not been disposed or reset.
    /// Throws an <see cref="ObjectDisposedException"/> if the arena is invalid.
    /// </summary>
    /// <param name="arena">The allocator to check.</param>
    /// <param name="generation">The generation snapshot taken at initialization.</param>
    /// <param name="typeName">The name of the type making the check (for the exception message).</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void CheckAliveThrowIfNot(ArenaAllocator arena, int generation, string typeName)
    {
        if (arena == null || arena.CurrentGeneration != generation)
        {
            throw new ObjectDisposedException(typeName, "Arena was reset or disposed — all pointers invalid");
        }
    }
}
