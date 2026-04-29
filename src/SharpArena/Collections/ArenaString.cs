using System.Runtime.CompilerServices;
using SharpArena.Allocators;
using System.Runtime.InteropServices;

// ReSharper disable MemberCanBePrivate.Global
namespace SharpArena.Collections;

/// <summary>
/// A non-owning view of UTF-16 text stored in unmanaged (arena) memory.
/// This struct is unmanaged and can be stored in arena-backed collections.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
[System.Diagnostics.DebuggerDisplay("{ToString()}")]
public readonly unsafe struct ArenaString : IEquatable<ArenaString>
{
    private readonly char* _ptr;
    private readonly int _len;
    private readonly int _generation;

    /// <summary>
    /// Performs an implicit conversion to <see cref="ReadOnlySpan{Char}"/>.
    /// </summary>
    /// <param name="value">The arena-backed string.</param>
    /// <returns>A span referencing the same characters.</returns>
    public static implicit operator ReadOnlySpan<char>(ArenaString value) => value.AsSpan();

    private ArenaString(char* ptr, int len, int generation)
    {
        _ptr = ptr;
        _len = len;
        _generation = generation;
    }

    internal ArenaString(ArenaAllocator arena, char* ptr, int len)
    {
        _generation = arena?.CurrentGeneration ?? 0;
        _ptr = ptr;
        _len = len;
    }

    /// <summary>
    /// Gets the current generation this string was allocated in.
    /// </summary>
    public int Generation => _generation;

    /// <summary>
    /// Gets a pointer to the raw character data.
    /// </summary>
    public char* RawPtr => _ptr;

    /// <summary>
    /// Checks if this string is still valid within the provided arena.
    /// </summary>
    /// <param name="arena">The arena to check against.</param>
    /// <returns><see langword="true"/> if the arena generation matches the string's allocation generation.</returns>
    public bool IsAlive(ArenaAllocator arena)
    {
        if (_ptr == null || _len == 0) return true;
        return arena.CurrentGeneration == _generation;
    }

    /// <summary>
    /// Throws an <see cref="ObjectDisposedException"/> if the string is no longer valid in the specified arena.
    /// </summary>
    /// <param name="arena">The arena that owns this string's memory.</param>
    public void Verify(ArenaAllocator arena)
    {
        if (_ptr == null || _len == 0) return;
        UnsafeHelpers.CheckAliveThrowIfNot(arena, _generation, nameof(ArenaString));
    }

    /// <summary>
    /// Gets the number of characters represented by the string.
    /// </summary>
    public int Length => _len;

    /// <summary>
    /// Gets a value indicating whether the string is empty or uninitialized.
    /// </summary>
    public bool IsEmpty => _len == 0 || _ptr == null;

    /// <summary>
    /// Materializes the arena-backed buffer as a managed span.
    /// </summary>
    /// <returns>The span covering the string contents.</returns>
    public ReadOnlySpan<char> AsSpan() => 
        _ptr == null ? ReadOnlySpan<char>.Empty : new ReadOnlySpan<char>(_ptr, _len);

    /// <summary>
    /// Returns the managed string representation of the arena-backed buffer.
    /// </summary>
    /// <returns>A managed string copy.</returns>
    public override string ToString() => 
        _ptr == null ? string.Empty : new string(_ptr, 0, _len);

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

        var charCount = (uint)src.Length;
        var byteCount = (nuint)charCount * (nuint)sizeof(char);
        
        var dest = (char*)arena.Alloc(byteCount, align: (nuint)UnsafeHelpers.AlignOf<char>());
        
        fixed (char* srcPtr = src)
        {
            Unsafe.CopyBlockUnaligned(dest, srcPtr, (uint)byteCount);
        }

        return new ArenaString(arena, dest, src.Length);
    }

    /// <summary>
    /// Determines whether the current string equals the provided span.
    /// </summary>
    /// <param name="other">The span to compare.</param>
    /// <returns><see langword="true"/> when the spans are equal; otherwise, <see langword="false"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(ReadOnlySpan<char> other)
    {
        if (_len != other.Length) return false;
        if (_ptr == null) return other.IsEmpty;

        return MemoryMarshal.AsBytes(AsSpan()).SequenceEqual(MemoryMarshal.AsBytes(other));
    }

    /// <summary>
    /// Determines whether the current string equals another arena-backed string.
    /// </summary>
    /// <param name="other">The other string to compare.</param>
    /// <returns><see langword="true"/> when the strings are equal; otherwise, <see langword="false"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(ArenaString other)
    {
        if (_ptr == other._ptr && _len == other._len) return true;
        return Equals(other.AsSpan());
    }

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

        return new ArenaString(_ptr + start, length, _generation);
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

    /// <summary>
    /// Concatenates two arena strings into a new arena string allocated in the same arena.
    /// </summary>
    /// <param name="left">The first string.</param>
    /// <param name="right">The second string.</param>
    /// <param name="arena">The allocator for the new string.</param>
    /// <returns>A new <see cref="ArenaString"/> containing the concatenated characters.</returns>
    public static ArenaString Concatenate(ArenaString left, ArenaString right, ArenaAllocator arena)
    {
        if (left.IsEmpty && right.IsEmpty)
        {
            return default;
        }

        if (left.IsEmpty)
        {
            return right;
        }

        if (right.IsEmpty)
        {
            return left;
        }

        if ((long)(uint)left._len + (long)(uint)right._len > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(left), "Concatenated string length exceeds maximum length.");
        }

        var newLen = left._len + right._len;
        var byteCount = (nuint)newLen * (nuint)sizeof(char);

        var dest = (char*)arena.Alloc(byteCount, align: (nuint)UnsafeHelpers.AlignOf<char>());

        Unsafe.CopyBlockUnaligned(dest, left._ptr, (uint)(left._len * sizeof(char)));
        Unsafe.CopyBlockUnaligned(dest + left._len, right._ptr, (uint)(right._len * sizeof(char)));

        return new ArenaString(arena, dest, newLen);
    }
}
