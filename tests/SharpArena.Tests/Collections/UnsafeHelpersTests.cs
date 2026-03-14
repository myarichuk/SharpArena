using System;
using System.Runtime.InteropServices;
using SharpArena.Collections;
using Xunit;

namespace SharpArena.Tests.Collections;

public class UnsafeHelpersTests
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct Size9Struct
    {
        public long A;
        public byte B;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct Size17Struct
    {
        public long A;
        public long B;
        public byte C;
    }

    [Fact]
    public void AlignOf_ReturnsAtLeastPointerSize()
    {
        int pointerSize = IntPtr.Size;

        Assert.True(UnsafeHelpers.AlignOf<byte>() >= pointerSize);
        Assert.True(UnsafeHelpers.AlignOf<short>() >= pointerSize);
        Assert.True(UnsafeHelpers.AlignOf<int>() >= pointerSize);
        Assert.True(UnsafeHelpers.AlignOf<long>() >= pointerSize);

        // Specific checks for types smaller than pointer size
        if (pointerSize == 8)
        {
            Assert.Equal(8, UnsafeHelpers.AlignOf<byte>());
            Assert.Equal(8, UnsafeHelpers.AlignOf<int>());
        }
        else if (pointerSize == 4)
        {
            Assert.Equal(4, UnsafeHelpers.AlignOf<byte>());
            Assert.Equal(4, UnsafeHelpers.AlignOf<short>());
        }
    }

    [Fact]
    public void AlignOf_ReturnsPowerOfTwo()
    {
        // Size 9 should round up to 16
        Assert.Equal(16, UnsafeHelpers.AlignOf<Size9Struct>());

        // Size 17 should round up to 32
        Assert.Equal(32, UnsafeHelpers.AlignOf<Size17Struct>());
    }
}
