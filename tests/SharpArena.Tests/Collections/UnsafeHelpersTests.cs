using System;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using SharpArena.Collections;
using Xunit;

namespace SharpArena.Tests.Collections;

public class UnsafeHelpersTests
{
    // Deliberately packed so the struct's own *size* is not a multiple of a "nice" power of two —
    // a stand-in for the C3 bug, where AlignOf<T> used to be `RoundUpToPowerOf2(sizeof(T))`. That
    // heuristic said a struct's alignment scales with its size, which is false: a 4 KB struct of
    // bytes needs no more than byte alignment, not 4 KB. The real, CLR-computed alignment for any
    // of these Pack = 1 structs (whose fields need no more than byte alignment) is 1, which the
    // floor below then raises to IntPtr.Size — regardless of the struct's size.
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct Size9Struct { public long A; public byte B; }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct Size17Struct { public long A; public long B; public byte C; }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct Size16Struct { public long A; public long B; }

    [Fact]
    public void AlignOf_AlwaysAtLeastPointerSize()
    {
        int ptr = IntPtr.Size;

        Assert.Equal(ptr, UnsafeHelpers.AlignOf<byte>());
        Assert.Equal(ptr, UnsafeHelpers.AlignOf<short>());
        Assert.Equal(ptr, UnsafeHelpers.AlignOf<int>());   // 4→8 on 64-bit, 4 on 32-bit
        Assert.Equal(8, UnsafeHelpers.AlignOf<long>());    // long is special
    }

    [Fact]
    public void AlignOf_IgnoresSize_UsesRealClrAlignment()
    {
        // A struct's *size* must not drive its alignment (that was the C3 bug: a 4 KB struct got
        // 4 KB alignment). All three structs here have different sizes (9, 16, 17 bytes) but the
        // same real field alignment (1, from Pack = 1), so AlignOf must return the same floored
        // value — IntPtr.Size — for all of them, not a value derived from rounding their size up.
        int ptr = IntPtr.Size;

        Assert.Equal(ptr, UnsafeHelpers.AlignOf<Size9Struct>());
        Assert.Equal(ptr, UnsafeHelpers.AlignOf<Size16Struct>());
        Assert.Equal(ptr, UnsafeHelpers.AlignOf<Size17Struct>());
    }

    [Fact]
    public void AlignOf_CommonRealTypes()
    {
        // Guid and decimal are both 16 bytes, but their fields are all int-sized (4 bytes), so
        // their real CLR alignment is 4 — floored up to IntPtr.Size here, not 16. The old
        // size-based heuristic got this wrong in the other direction (it said 16).
        int ptr = IntPtr.Size;

        Assert.Equal(8, UnsafeHelpers.AlignOf<double>());
        Assert.Equal(ptr, UnsafeHelpers.AlignOf<Guid>());
        Assert.Equal(ptr, UnsafeHelpers.AlignOf<decimal>());
    }

    [Fact]
    public void AlignOf_DetectsAlignmentAboveIntPtrSize_ForRealSimdTypes()
    {
        // Vector128<T>/Vector256<T> are the rare unmanaged types whose actual CLR-computed
        // alignment genuinely exceeds IntPtr.Size (16 and 32 bytes respectively, matching their
        // SIMD register width) — this is the one case the IntPtr.Size floor must not swallow.
        Assert.Equal(16, UnsafeHelpers.AlignOf<Vector128<byte>>());
        Assert.Equal(32, UnsafeHelpers.AlignOf<Vector256<byte>>());
    }
}
