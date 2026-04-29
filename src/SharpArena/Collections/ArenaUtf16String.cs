using System.Runtime.CompilerServices;
using SharpArena.Allocators;
using System.Runtime.InteropServices;

namespace SharpArena.Collections;

/// <summary>
/// A non-owning view of UTF-16 text stored in unmanaged (arena) memory.
/// This struct is unmanaged and can be stored in arena-backed collections.
/// Consistent with <see cref="ArenaUtf8String"/> naming.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
[System.Diagnostics.DebuggerDisplay("{ToString()}")]
public readonly unsafe struct ArenaUtf16String : IEquatable<ArenaUtf16String>
{
    private readonly char* _ptr;
    private readonly int _len;
    private readonly int _generation;

    /// <summary>
    /// Performs an implicit conversion to <see cref="ReadOnlySpan{Char}"/>.
    /// </summary>
    public static implicit operator ReadOnlySpan<char>(ArenaUtf16String value) => value.AsSpan();

    private ArenaUtf16String(char* ptr, int len, int generation)
    {
        _ptr = ptr;
        _len = len;
        _generation = generation;
    }

    internal ArenaUtf16String(ArenaAllocator arena, char* ptr, int len)
    {
        _generation = arena?.CurrentGeneration ?? 0;
        _ptr = ptr;
        _len = len;
    }

    public int Generation => _generation;
    public char* RawPtr => _ptr;
    public int Length => _len;
    public bool IsEmpty => _len == 0 || _ptr == null;

    public ReadOnlySpan<char> AsSpan() => 
        _ptr == null ? ReadOnlySpan<char>.Empty : new ReadOnlySpan<char>(_ptr, _len);

    public override string ToString() => 
        _ptr == null ? string.Empty : new string(_ptr, 0, _len);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ArenaUtf16String Clone(ReadOnlySpan<char> src, ArenaAllocator arena)
    {
        if (src.IsEmpty) return default;
        var byteCount = (nuint)src.Length * (nuint)sizeof(char);
        var dest = (char*)arena.Alloc(byteCount, align: (nuint)UnsafeHelpers.AlignOf<char>());
        fixed (char* srcPtr = src)
        {
            Unsafe.CopyBlockUnaligned(dest, srcPtr, (uint)byteCount);
        }
        return new ArenaUtf16String(arena, dest, src.Length);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(ReadOnlySpan<char> other)
    {
        if (_len != other.Length) return false;
        if (_ptr == null) return other.IsEmpty;
        return AsSpan().SequenceEqual(other);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(ArenaUtf16String other)
    {
        if (_ptr == other._ptr && _len == other._len) return true;
        return Equals(other.AsSpan());
    }

    public override bool Equals(object? obj) => obj is ArenaUtf16String s && Equals(s);

    public override int GetHashCode()
    {
        if (_ptr == null || _len == 0) return 0;
        return (int)System.IO.Hashing.XxHash3.HashToUInt64(MemoryMarshal.AsBytes(AsSpan()));
    }

    public static bool operator ==(ArenaUtf16String left, ArenaUtf16String right) => left.Equals(right);
    public static bool operator !=(ArenaUtf16String left, ArenaUtf16String right) => !left.Equals(right);
}
