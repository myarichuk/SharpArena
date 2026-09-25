using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SharpArena.Allocators;

namespace SharpArena.Collections;

/// <summary>
/// Provides helper methods for unsafe and memory alignment operations.
/// </summary>
internal static class UnsafeHelpers
{
    [StructLayout(LayoutKind.Sequential)]
    private struct AlignHelper<TValue> where TValue : unmanaged
    {
        public byte B;
        public TValue Value;
    }

    /// <summary>
    /// Computes the actual memory alignment requirement the runtime uses for type T,
    /// by measuring where the CLR places a <typeparamref name="T"/> field after a single
    /// leading byte (rather than guessing from the type's size).
    /// </summary>
    /// <typeparam name="T">The unmanaged type to determine alignment for.</typeparam>
    /// <returns>The alignment size in bytes, never smaller than <see cref="IntPtr.Size"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int AlignOf<T>() where T : unmanaged
    {
        var natural = Unsafe.SizeOf<AlignHelper<T>>() - Unsafe.SizeOf<T>();

        // Assume at least pointer-size alignment (worst case bit over-align, but always safe).
        return natural < IntPtr.Size ? IntPtr.Size : natural;
    }

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
