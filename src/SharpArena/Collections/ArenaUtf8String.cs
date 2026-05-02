using System.Buffers;
using System.Runtime.CompilerServices;
using SharpArena.Allocators;
using System.Runtime.InteropServices;
using System.Text;

namespace SharpArena.Collections;

/// <summary>
/// A non-owning view of UTF-8 text stored in unmanaged (arena) memory.
/// This struct is unmanaged and can be stored in arena-backed collections.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
[System.Diagnostics.DebuggerDisplay("{ToString()}")]
public readonly unsafe struct ArenaUtf8String : IEquatable<ArenaUtf8String>
{
    private readonly byte* _ptr;
    private readonly int _len;
    private readonly int _generation;

    /// <summary>
    /// Performs an implicit conversion to <see cref="ReadOnlySpan{Byte}"/>.
    /// </summary>
    /// <param name="value">The arena-backed string.</param>
    /// <returns>A span referencing the same bytes.</returns>
    public static implicit operator ReadOnlySpan<byte>(ArenaUtf8String value) => value.AsSpan();

    internal ArenaUtf8String(ArenaAllocator arena, byte* ptr, int len)
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
    /// Gets a pointer to the raw byte data.
    /// </summary>
    public byte* RawPtr => _ptr;

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
        UnsafeHelpers.CheckAliveThrowIfNot(arena, _generation, nameof(ArenaUtf8String));
    }

    /// <summary>
    /// Gets the number of bytes represented by the string.
    /// </summary>
    public int Length => _len;

    /// <summary>
    /// Gets a value indicating whether the string is empty or uninitialized.
    /// </summary>
    public bool IsEmpty => _len == 0 || _ptr == null;

    /// <summary>
    /// Materializes the arena-backed buffer as a managed span.
    /// </summary>
    /// <returns>A span covering the string contents.</returns>
    public ReadOnlySpan<byte> AsSpan() => 
        _ptr == null ? ReadOnlySpan<byte>.Empty : new ReadOnlySpan<byte>(_ptr, _len);

    /// <summary>
    /// Returns the managed string representation of the arena-backed buffer.
    /// </summary>
    /// <returns>A managed string copy.</returns>
    public override string ToString() => 
        _ptr == null ? string.Empty : Encoding.UTF8.GetString(_ptr, _len);

    /// <summary>
    /// Clones the provided span into arena-managed memory.
    /// </summary>
    /// <param name="src">The source string to copy.</param>
    /// <param name="arena">Allocator providing unmanaged storage.</param>
    /// <returns>An <see cref="ArenaUtf8String"/> referencing the cloned bytes.</returns>    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ArenaUtf8String Clone(string? src, ArenaAllocator arena)
    {
        if (string.IsNullOrEmpty(src))
        {
            return default;
        }

        var byteCount = Encoding.UTF8.GetByteCount(src);
        if (byteCount == 0)
        {
            return default;
        }

        var dest = (byte*)arena.Alloc((uint)byteCount, align: 1);
        fixed (char* srcPtr = src)
        {
            Encoding.UTF8.GetBytes(
                new ReadOnlySpan<char>(srcPtr, src.Length),
                new Span<byte>(dest, byteCount));
        }

        return new ArenaUtf8String(arena, dest, byteCount);
    }
    
    /// <summary>
    /// Clones the provided span into arena-managed memory.
    /// </summary>
    /// <param name="src">The source string to copy</param>
    /// <param name="arena">Allocator providing unmanaged storage.</param>
    /// <returns>An <see cref="ArenaUtf8String"/> referencing the cloned bytes.</returns>        
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ArenaUtf8String Clone(ReadOnlySpan<char> src, ArenaAllocator arena)
    {
        if (src.IsEmpty)
        {
            return default;
        }

        var byteCount = Encoding.UTF8.GetByteCount(src);
        if (byteCount == 0)
        {
            return default;
        }

        var dest = (byte*)arena.Alloc((uint)byteCount, align: 1);
        Encoding.UTF8.GetBytes(
            src,
            new Span<byte>(dest, byteCount));

        return new ArenaUtf8String(arena, dest, byteCount);
    }    
    
    /// <summary>
    /// Clones the provided span into arena-managed memory.
    /// </summary>
    /// <param name="src">The source bytes to copy.</param>
    /// <param name="arena">Allocator providing unmanaged storage.</param>
    /// <returns>An <see cref="ArenaUtf8String"/> referencing the cloned bytes.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ArenaUtf8String Clone(ReadOnlySpan<byte> src, ArenaAllocator arena)
    {
        if (src.IsEmpty) return default;
        var dest = (byte*)arena.Alloc((uint)src.Length, align: 1);
        fixed (byte* srcPtr = src)
        {
            Unsafe.CopyBlockUnaligned(dest, srcPtr, (uint)src.Length);
        }
        return new ArenaUtf8String(arena, dest, src.Length);
    }

    /// <summary>
    /// Determines whether the current string equals the provided span.
    /// </summary>
    /// <param name="other">The span to compare.</param>
    /// <returns><see langword="true"/> when the spans are equal; otherwise, <see langword="false"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(ReadOnlySpan<byte> other)
    {
        if (_len != other.Length) return false;
        if (_ptr == null) return other.IsEmpty;
        return AsSpan().SequenceEqual(other);
    }

    /// <summary>
    /// Determines whether the current string equals another arena-backed UTF-8 string.
    /// </summary>
    /// <param name="other">The other string to compare.</param>
    /// <returns><see langword="true"/> when the strings are equal; otherwise, <see langword="false"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(ArenaUtf8String other)
    {
        if (_ptr == other._ptr && _len == other._len) return true;
        return Equals(other.AsSpan());
    }

    /// <summary>
    /// Compare to a managed string.
    /// </summary>
    /// <param name="other">managed string to compare to</param>   
    /// <returns>true if strings are equal, false otherwise</returns>
    public bool Equals(string? other)
    {
        if (other == null)
        {
            return IsEmpty;
        }

        if (IsEmpty)
        {
            return false;
        }

        // Fast path: ASCII-only strings (very common)
        if (IsAscii(other))
        {
            if (_len != other.Length)
            {
                return false;
            }

            fixed (char* src = other)
            {
                for (int i = 0; i < _len; i++)
                {
                    if (_ptr[i] != (byte)src[i]) return false;
                }
            }
            return true;
        }

        int maxBytes = Encoding.UTF8.GetMaxByteCount(other.Length);

        if (maxBytes <= 512)
        {
            Span<byte> buffer = stackalloc byte[maxBytes];
            var written = Encoding.UTF8.GetBytes(other, buffer);
            return AsSpan().SequenceEqual(buffer.Slice(0, written)); // ← SIMD here
        }

        // for large strings -> no choice but ArrayPool
        byte[]? rented = null;
        try
        {
            rented = ArrayPool<byte>.Shared.Rent(maxBytes);
            var written = Encoding.UTF8.GetBytes(other, rented.AsSpan());
            return AsSpan().SequenceEqual(new ReadOnlySpan<byte>(rented, 0, written));
        }
        finally
        {
            if (rented != null)
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsAscii(string s)
    {
        for (var index = 0; index < s.Length; index++)
        {
            var c = s[index];
            if (c > 127)
            {
                return false;
            }
        }

        return true;
    }    
    
    /// <summary>
    /// Decodes the UTF-8 content into the provided character buffer.
    /// Returns the number of characters written.
    /// </summary>
    public int DecodeTo(Span<char> destination) => 
        Encoding.UTF8.GetChars(AsSpan(), destination);

    /// <inheritdoc />
    public override bool Equals(object? obj) => 
        obj is ArenaUtf8String @as && Equals(@as) ||
        obj is string s && Equals(s);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        if (_ptr == null || _len == 0) return 0;
        return (int)System.IO.Hashing.XxHash3.HashToUInt64(AsSpan());
    }

    /// <summary>
    /// Determines whether two arena strings refer to the same sequence of bytes.
    /// </summary>
    /// <param name="left">The first string.</param>
    /// <param name="right">The second string.</param>
    /// <returns><see langword="true"/> when both strings are equal; otherwise, <see langword="false"/>.</returns>
    public static bool operator ==(ArenaUtf8String left, ArenaUtf8String right) => left.Equals(right);

    /// <summary>
    /// Determines whether two arena strings refer to different byte sequences.
    /// </summary>
    /// <param name="left">The first string.</param>
    /// <param name="right">The second string.</param>
    /// <returns><see langword="true"/> when the strings differ; otherwise, <see langword="false"/>.</returns>
    public static bool operator !=(ArenaUtf8String left, ArenaUtf8String right) => !left.Equals(right);
}
