using System.Runtime.CompilerServices;
using SharpArena.Allocators;
using System.Runtime.InteropServices;

// ReSharper disable MemberCanBePrivate.Global
namespace SharpArena.Collections;

/// <summary>
/// A non-owning view of UTF-16 text stored in unmanaged (arena) memory.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly unsafe struct ArenaString(char* ptr, int len)
{
    /// <summary>
    /// Performs an implicit conversion to <see cref="ReadOnlySpan{Char}"/>.
    /// </summary>
    /// <param name="value">The arena-backed string.</param>
    /// <returns>A span referencing the same characters.</returns>
    public static implicit operator ReadOnlySpan<char>(ArenaString value) => value.AsSpan();

    /// <summary>
    /// Gets the number of characters represented by the string.
    /// </summary>
    public int Length => len;

    /// <summary>
    /// Gets a value indicating whether the string is empty or uninitialized.
    /// </summary>
    public bool IsEmpty => len == 0 || ptr == null;

    /// <summary>
    /// Materializes the arena-backed buffer as a managed span.
    /// </summary>
    /// <returns>The span covering the string contents.</returns>
    public ReadOnlySpan<char> AsSpan() =>
        ptr == null ? ReadOnlySpan<char>.Empty : new ReadOnlySpan<char>(ptr, len);

    /// <summary>
    /// Returns the managed string representation of the arena-backed buffer.
    /// </summary>
    /// <returns>A managed string copy.</returns>
    public override string ToString() =>
        ptr == null ? string.Empty : new string(ptr, 0, len);

    /// <summary>
    /// Clones the provided span into arena-managed memory.
    /// </summary>
    /// <param name="src">The source characters to copy.</param>
    /// <param name="arena">Allocator providing unmanaged storage.</param>
    /// <returns>An <see cref="ArenaString"/> referencing the cloned characters.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ArenaString Clone(ReadOnlySpan<char> src, ArenaAllocator arena)
    {
        if (src.IsEmpty)
        {
            return default;
        }

        var bytes = (nuint)(src.Length * sizeof(char));
        var dest = (char*)arena.Alloc(bytes, align: (nuint)UnsafeHelpers.AlignOf<char>());
        src.CopyTo(new Span<char>(dest, src.Length));
        return new ArenaString(dest, src.Length);
    }

    /// <summary>
    /// Determines whether the current string equals the provided span.
    /// </summary>
    /// <param name="other">The span to compare.</param>
    /// <returns><see langword="true"/> when the spans are equal; otherwise, <see langword="false"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(ReadOnlySpan<char> other) =>
        len == other.Length && AsSpan().SequenceEqual(other);

    /// <summary>
    /// Determines whether the current string equals another arena-backed string.
    /// </summary>
    /// <param name="other">The other string to compare.</param>
    /// <returns><see langword="true"/> when the strings are equal; otherwise, <see langword="false"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(ArenaString other) =>
        Equals(other.AsSpan());

    /// <inheritdoc />
    public override bool Equals(object? obj) =>
        obj is ArenaString s && Equals(s);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine((nint)ptr, len);

    /// <summary>
    /// Creates a new <see cref="ArenaString"/> that references a subrange of the current buffer.
    /// </summary>
    /// <param name="start">The zero-based inclusive start index.</param>
    /// <param name="length">The number of characters in the slice.</param>
    /// <returns>A new arena string referencing the requested subrange.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="start"/>, <paramref name="length"/>, or their range exceeds the current string bounds.
    /// </exception>
    public ArenaString Slice(int start, int length)
    {
        if (start < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(start), "Start index must be non-negative.");
        }

        if (length < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(length), "Slice length must be non-negative.");
        }

        if (start + length > len)
        {
            throw new ArgumentOutOfRangeException(nameof(length), "Slice range must be within the string bounds.");
        }

        return new ArenaString(ptr + start, length);
    }

    /// <summary>
    /// Determines whether two arena strings refer to the same sequence of characters.
    /// </summary>
    /// <param name="left">The first string.</param>
    /// <param name="right">The second string.</param>
    /// <returns><see langword="true"/> when both strings are equal; otherwise, <see langword="false"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(ArenaString left, ArenaString right) => left.Equals(right);

    /// <summary>
    /// Determines whether two arena strings refer to different character sequences.
    /// </summary>
    /// <param name="left">The first string.</param>
    /// <param name="right">The second string.</param>
    /// <returns><see langword="true"/> when the strings differ; otherwise, <see langword="false"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(ArenaString left, ArenaString right) => !left.Equals(right);
}
