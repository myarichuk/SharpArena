using SharpArena.Allocators;
using SharpArena.Collections;
using Xunit;

namespace Json.Path.Tests;


public unsafe class NativeAllocatorTests
{
    private const nuint PageSize = 4096;

    [Theory]
    [InlineData(NativeAllocatorBackend.DotNetUnmanaged)]
    [InlineData(NativeAllocatorBackend.PlatformInvoke)]
    public void AllocAndFree_ShouldWork(NativeAllocatorBackend backend)
    {
        var ptr = NativeAllocator.Alloc(PageSize, backend);
        Assert.NotEqual(IntPtr.Zero, (IntPtr)ptr);

        var span = new Span<byte>(ptr, (int)PageSize);
        span.Fill(0xDE);
        Assert.All(span[..64].ToArray(), b => Assert.Equal(0xDE, b));

        NativeAllocator.Free(ptr, backend);
    }

    [Theory]
    [InlineData(NativeAllocatorBackend.DotNetUnmanaged)]
    [InlineData(NativeAllocatorBackend.PlatformInvoke)]
    public void MultipleAllocations_ShouldBeIndependent(NativeAllocatorBackend backend)
    {
        var ptr1 = NativeAllocator.Alloc(PageSize, backend);
        var ptr2 = NativeAllocator.Alloc(PageSize, backend);

        Assert.NotEqual((nint)ptr1, (nint)ptr2);

        var span1 = new Span<byte>(ptr1, (int)PageSize);
        var span2 = new Span<byte>(ptr2, (int)PageSize);

        span1.Fill(0x01);
        span2.Fill(0x02);

        Assert.Equal(0x01, span1[0]);
        Assert.Equal(0x02, span2[0]);

        NativeAllocator.Free(ptr1, backend);
        NativeAllocator.Free(ptr2, backend);
    }

    [Fact]
    public void Alloc_ZeroSize_ShouldReturnNull()
    {
        var ptr = NativeAllocator.Alloc(0, NativeAllocatorBackend.DotNetUnmanaged);
        Assert.Equal(IntPtr.Zero, (IntPtr)ptr);
    }

    [Theory]
    [InlineData(NativeAllocatorBackend.DotNetUnmanaged)]
    [InlineData(NativeAllocatorBackend.PlatformInvoke)]
    public void DoubleFree_ShouldNotCrash(NativeAllocatorBackend backend)
    {
        var ptr = NativeAllocator.Alloc(PageSize, backend);
        NativeAllocator.Free(ptr, backend);

        var ex = Record.Exception(() => NativeAllocator.Free(ptr, backend));
        Assert.NotNull(ex);
        Assert.IsType<InvalidOperationException>(ex);
    }
}
