using System.Runtime.CompilerServices;
using SharpArena.Allocators;
using System.Runtime.InteropServices;

// ReSharper disable MemberCanBePrivate.Global
namespace SharpArena.Collections;

/// <summary>
/// A non-owning view of UTF-16 text stored in unmanaged (arena) memory.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly unsafe struct ArenaString
{
    private readonly char* _ptr;
    private readonly int _len;
    private readonly ArenaAllocator _arena;
    private readonly int _generation;

    /// <summary>
    /// Performs an implicit conversion to <see cref="ReadOnlySpan{Char}"/>.
    /// </summary>
    /// <param name="value">The arena-backed string.</param>
    /// <returns>A span referencing the same characters.</returns>
    public static implicit operator ReadOnlySpan<char>(ArenaString value) => value.AsSpan();

    internal ArenaString(ArenaAllocator arena, char* ptr, int len)
    {
        _arena = arena;
        _generation = arena?.CurrentGeneration ?? 0;
        _ptr = ptr;
        _len = len;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void CheckAliveThrowIfNot()
    {
        if (_ptr == null || _len == 0)
        {
            return;
        }

        UnsafeHelpers.CheckAliveThrowIfNot(_arena, _generation, nameof(ArenaString));
    }

    /// <summary>
    /// Gets the number of characters represented by the string.
    /// </summary>
    public int Length
    {
        get
        {
            CheckAliveThrowIfNot();
            return _len;
        }
    }

    /// <summary>
    /// Gets a value indicating whether the string is empty or uninitialized.
    /// </summary>
    public bool IsEmpty
    {
        get
        {
            CheckAliveThrowIfNot();
            return _len == 0 || _ptr == null;
        }
    }

    /// <summary>
    /// Materializes the arena-backed buffer as a managed span.
    /// </summary>
    /// <returns>The span covering the string contents.</returns>
    public ReadOnlySpan<char> AsSpan()
    {
        CheckAliveThrowIfNot();
        return _ptr == null ? ReadOnlySpan<char>.Empty : new ReadOnlySpan<char>(_ptr, _len);
    }

    /// <summary>
    /// Returns the managed string representation of the arena-backed buffer.
    /// </summary>
    /// <returns>A managed string copy.</returns>
    public override string ToString()
    {
        CheckAliveThrowIfNot();
        return _ptr == null ? string.Empty : new string(_ptr, 0, _len);
    }

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

        ulong bytes = (ulong)(uint)src.Length * (ulong)sizeof(char);
        if (bytes != (ulong)(nuint)bytes)
        {
            throw new ArgumentOutOfRangeException(nameof(src), "Source span size exceeds addressable memory.");
        }

        var dest = (char*)arena.Alloc((nuint)bytes, align: (nuint)UnsafeHelpers.AlignOf<char>());
        src.CopyTo(new Span<char>(dest, src.Length));
        return new ArenaString(arena, dest, src.Length);
    }

    /// <summary>
    /// Determines whether the current string equals the provided span.
    /// </summary>
    /// <param name="other">The span to compare.</param>
    /// <returns><see langword="true"/> when the spans are equal; otherwise, <see langword="false"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(ReadOnlySpan<char> other) =>
        _len == other.Length && AsSpan().SequenceEqual(other);

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
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(_len);
#if NETCOREAPP3_0_OR_GREATER || NET
        hash.AddBytes(MemoryMarshal.AsBytes(AsSpan()));
#else
        foreach (var ch in AsSpan())
        {
            hash.Add(ch);
        }
#endif
        return hash.ToHashCode();
    }

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
        CheckAliveThrowIfNot();
        if (start < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(start), "Start index must be non-negative.");
        }

        if (length < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(length), "Slice length must be non-negative.");
        }

        if (length > _len - start)
        {
            throw new ArgumentOutOfRangeException(nameof(length), "Slice range must be within the string bounds.");
        }

        return new ArenaString(_arena, _ptr + start, length);
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
