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
    public static implicit operator ReadOnlySpan<byte>(ArenaUtf8String value) => value.AsSpan();

    private ArenaUtf8String(byte* ptr, int len, int generation)
    {
        _ptr = ptr;
        _len = len;
        _generation = generation;
    }

    internal ArenaUtf8String(ArenaAllocator arena, byte* ptr, int len)
    {
        _generation = arena?.CurrentGeneration ?? 0;
        _ptr = ptr;
        _len = len;
    }

    public int Generation => _generation;
    public byte* RawPtr => _ptr;
    public int Length => _len;
    public bool IsEmpty => _len == 0 || _ptr == null;

    public ReadOnlySpan<byte> AsSpan() => 
        _ptr == null ? ReadOnlySpan<byte>.Empty : new ReadOnlySpan<byte>(_ptr, _len);

    public override string ToString() => 
        _ptr == null ? string.Empty : Encoding.UTF8.GetString(_ptr, _len);

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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(ReadOnlySpan<byte> other)
    {
        if (_len != other.Length) return false;
        if (_ptr == null) return other.IsEmpty;
        return AsSpan().SequenceEqual(other);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(ArenaUtf8String other)
    {
        if (_ptr == other._ptr && _len == other._len) return true;
        return Equals(other.AsSpan());
    }

    public override bool Equals(object? obj) => obj is ArenaUtf8String s && Equals(s);

    public override int GetHashCode()
    {
        if (_ptr == null || _len == 0) return 0;
        return (int)System.IO.Hashing.XxHash3.HashToUInt64(AsSpan());
    }

    public static bool operator ==(ArenaUtf8String left, ArenaUtf8String right) => left.Equals(right);
    public static bool operator !=(ArenaUtf8String left, ArenaUtf8String right) => !left.Equals(right);
}
