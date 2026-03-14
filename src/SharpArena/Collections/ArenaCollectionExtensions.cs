using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SharpArena.Allocators;

namespace SharpArena.Collections;

/// <summary>
/// Provides extension methods for creating arena collections from spans.
/// </summary>
public static class ArenaCollectionExtensions
{
    /// <summary>
    /// Creates a new <see cref="ArenaList{T}"/> allocated from the specified arena, containing the elements from the source span.
    /// </summary>
    /// <typeparam name="T">The unmanaged element type.</typeparam>
    /// <param name="src">The source span to copy.</param>
    /// <param name="arena">The allocator providing storage.</param>
    /// <returns>An <see cref="ArenaList{T}"/> containing a copy of the elements.</returns>
    public static unsafe ArenaList<T> ToArenaList<T>(this ReadOnlySpan<T> src, ArenaAllocator arena) where T : unmanaged
    {
        if (arena == null) throw new ArgumentNullException(nameof(arena));

        var list = new ArenaList<T>(arena, initialCapacity: src.Length > 0 ? src.Length : 1);

        if (src.IsEmpty)
        {
            return list;
        }

        list.Header->Count = src.Length;

        ulong bytesToCopy = (ulong)(uint)src.Length * (ulong)sizeof(T);
        fixed (T* ptr = src)
        {
            System.Buffer.MemoryCopy(ptr, list.Header->Data, bytesToCopy, bytesToCopy);
        }

        return list;
    }

    /// <summary>
    /// Creates a new <see cref="ArenaList{T}"/> allocated from the specified arena, containing the elements from the source span.
    /// </summary>
    /// <typeparam name="T">The unmanaged element type.</typeparam>
    /// <param name="src">The source span to copy.</param>
    /// <param name="arena">The allocator providing storage.</param>
    /// <returns>An <see cref="ArenaList{T}"/> containing a copy of the elements.</returns>
    public static ArenaList<T> ToArenaList<T>(this Span<T> src, ArenaAllocator arena) where T : unmanaged
    {
        return ((ReadOnlySpan<T>)src).ToArenaList(arena);
    }
}
