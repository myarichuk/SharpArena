using System.Runtime.InteropServices;
using SharpArena.Allocators;
using Xunit;

namespace SharpArena.Tests.Allocators;

public unsafe class ArenaSegmentTests
{
    [Fact]
    public void TryAlloc_WithZeroAlignment_ShouldUseIntPtrSizeAlignment()
    {
        nuint bufferSize = 64;
        byte* buffer = (byte*)NativeMemory.Alloc(bufferSize);
        try
        {
            var segment = new ArenaSegment
            {
                Base = buffer,
                Size = bufferSize,
                Offset = 1 // Unaligned offset to test alignment behavior
            };

#if DEBUG
            segment.HeadCanary = 0xDEADBEEFCAFEBABEul;
            segment.TailCanary = 0xDEADBEEFCAFEBABEul;
#endif

            // Act: align = 0
            bool result = segment.TryAlloc(16, 0, out void* ptr);

            // Assert
            Assert.True(result, "TryAlloc should succeed");

            var address = (nuint)ptr;
            var expectedAlignment = (nuint)IntPtr.Size;

            Assert.True(address % expectedAlignment == 0, $"Pointer {address} should be aligned to {expectedAlignment}");

            var expectedOffset = (1 + expectedAlignment - 1) & ~(expectedAlignment - 1);
            Assert.Equal((nuint)buffer + expectedOffset, address);
            Assert.Equal(expectedOffset + 16, segment.Offset);
        }
        finally
        {
            NativeMemory.Free(buffer);
        }
    }
}
