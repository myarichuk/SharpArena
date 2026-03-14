using System;
using System.Runtime.InteropServices;
using SharpArena.Collections;
using Xunit;

namespace SharpArena.Tests.Collections;

public class UnsafeHelpersTests
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct Size9Struct { public long A; public byte B; }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct Size17Struct { public long A; public long B; public byte C; }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct Size16Struct { public long A; public long B; } // already power-of-2

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
    public void AlignOf_ReturnsNextPowerOfTwo()
    {
        Assert.Equal(16, UnsafeHelpers.AlignOf<Size16Struct>()); // unchanged
        Assert.Equal(16, UnsafeHelpers.AlignOf<Size9Struct>());  // 9→16
        Assert.Equal(32, UnsafeHelpers.AlignOf<Size17Struct>()); // 17→32
    }

    [Fact]
    public void AlignOf_CommonRealTypes()
    {
        Assert.Equal(8, UnsafeHelpers.AlignOf<double>());
        Assert.Equal(16, UnsafeHelpers.AlignOf<Guid>());
        // decimal is 16 on all platforms
        Assert.Equal(16, UnsafeHelpers.AlignOf<decimal>());
    }
}