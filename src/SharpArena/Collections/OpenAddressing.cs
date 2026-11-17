using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text;

namespace SharpArena.Collections;

/// <summary>
/// Shared linear-probing machinery for <see cref="ArenaDictionary{TKey,TValue}"/> and <see cref="ArenaSet{T}"/>.
/// </summary>
/// <remarks>
/// Layout: <c>buckets</c> is a power-of-two table of 1-based entry indices (0 = empty); entries
/// (keys, their cached hashes and, for the dictionary, values) are stored densely in insertion order.
/// Caching the hash per entry means a probe only calls <c>Equals</c> on a full 32-bit hash match
/// (string keys otherwise pay a length check + memcmp per colliding slot), and a resize never has
/// to re-hash keys (for string keys that is a full XxHash3 pass over every stored string, per resize).
/// </remarks>
internal static unsafe class OpenAddressing
{
    // Stack buffer for cross-encoding span lookups; beyond this an ArrayPool buffer is rented.
    private const int StackBufferBytes = 512;

    /// <summary>Returns the bucket index holding <paramref name="key"/>, or the empty bucket where it would be inserted.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int FindSlot<TKey>(int* buckets, uint* hashes, TKey* keys, uint mask, in TKey key, uint hash)
        where TKey : unmanaged, IEquatable<TKey>
    {
        uint index = hash & mask;
        while (true)
        {
            int entryIdx = buckets[index] - 1;
            if (entryIdx < 0 || (hashes[entryIdx] == hash && keys[entryIdx].Equals(key)))
            {
                return (int)index;
            }

            index = (index + 1) & mask;
        }
    }

    /// <summary>Re-populates a cleared bucket table from the dense entry hashes.</summary>
    public static void Rebuild(int* buckets, uint* hashes, int count, uint mask)
    {
        for (int i = 0; i < count; i++)
        {
            uint idx = hashes[i] & mask;
            while (buckets[idx] != 0) idx = (idx + 1) & mask;
            buckets[idx] = i + 1;
        }
    }

    /// <summary>
    /// Looks up a UTF-16 span against a table whose keys are <typeparamref name="TKey"/>
    /// (<see cref="ArenaUtf16String"/> directly, or <see cref="ArenaUtf8String"/> after transcoding).
    /// </summary>
    /// <returns>The entry index, or -1 when not found.</returns>
    public static int FindEntry<TKey>(int* buckets, uint* hashes, TKey* keys, uint mask, ReadOnlySpan<char> key)
        where TKey : unmanaged
    {
        if (typeof(TKey) == typeof(ArenaUtf16String))
        {
            return FindUtf16(buckets, hashes, (ArenaUtf16String*)keys, mask, key);
        }

        if (typeof(TKey) == typeof(ArenaUtf8String))
        {
            // Cross-encoding lookup: the table stores UTF-8, the caller has UTF-16 — transcode once.
            int maxBytes = Encoding.UTF8.GetMaxByteCount(key.Length);
            byte[]? rented = null;
            Span<byte> buffer = maxBytes <= StackBufferBytes
                ? stackalloc byte[StackBufferBytes]
                : (rented = ArrayPool<byte>.Shared.Rent(maxBytes));
            try
            {
                int written = Encoding.UTF8.GetBytes(key, buffer);
                return FindUtf8(buckets, hashes, (ArenaUtf8String*)keys, mask, buffer.Slice(0, written));
            }
            finally
            {
                if (rented != null) ArrayPool<byte>.Shared.Return(rented);
            }
        }

        throw new NotSupportedException($"{typeof(TKey)} does not support lookup by {nameof(ReadOnlySpan<char>)}.");
    }

    /// <summary>
    /// Looks up a UTF-8 span against a table whose keys are <typeparamref name="TKey"/>
    /// (<see cref="ArenaUtf8String"/> directly, or <see cref="ArenaUtf16String"/> after transcoding).
    /// </summary>
    /// <returns>The entry index, or -1 when not found.</returns>
    public static int FindEntry<TKey>(int* buckets, uint* hashes, TKey* keys, uint mask, ReadOnlySpan<byte> key)
        where TKey : unmanaged
    {
        if (typeof(TKey) == typeof(ArenaUtf8String))
        {
            return FindUtf8(buckets, hashes, (ArenaUtf8String*)keys, mask, key);
        }

        if (typeof(TKey) == typeof(ArenaUtf16String))
        {
            // Cross-encoding lookup: the table stores UTF-16, the caller has UTF-8 — transcode once.
            int maxChars = Encoding.UTF8.GetMaxCharCount(key.Length);
            char[]? rented = null;
            Span<char> buffer = maxChars <= StackBufferBytes / sizeof(char)
                ? stackalloc char[StackBufferBytes / sizeof(char)]
                : (rented = ArrayPool<char>.Shared.Rent(maxChars));
            try
            {
                int written = Encoding.UTF8.GetChars(key, buffer);
                return FindUtf16(buckets, hashes, (ArenaUtf16String*)keys, mask, buffer.Slice(0, written));
            }
            finally
            {
                if (rented != null) ArrayPool<char>.Shared.Return(rented);
            }
        }

        throw new NotSupportedException($"{typeof(TKey)} does not support lookup by {nameof(ReadOnlySpan<byte>)}.");
    }

    private static int FindUtf16(int* buckets, uint* hashes, ArenaUtf16String* keys, uint mask, ReadOnlySpan<char> key)
    {
        uint hash = Hashing.HashString(key);
        uint index = hash & mask;
        while (true)
        {
            int entryIdx = buckets[index] - 1;
            if (entryIdx < 0) return -1;
            if (hashes[entryIdx] == hash && keys[entryIdx].Equals(key)) return entryIdx;
            index = (index + 1) & mask;
        }
    }

    private static int FindUtf8(int* buckets, uint* hashes, ArenaUtf8String* keys, uint mask, ReadOnlySpan<byte> key)
    {
        uint hash = Hashing.HashUtf8(key);
        uint index = hash & mask;
        while (true)
        {
            int entryIdx = buckets[index] - 1;
            if (entryIdx < 0) return -1;
            if (hashes[entryIdx] == hash && keys[entryIdx].Equals(key)) return entryIdx;
            index = (index + 1) & mask;
        }
    }
}
