using SharpArena.Allocators;
using Xunit;

namespace SharpArena.Tests.Allocators;


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
    public void Alloc_Overflow_ShouldThrowOutOfMemoryException(NativeAllocatorBackend backend)
    {
#if NET7_0_OR_GREATER
        nuint max = nuint.MaxValue;
#else
        nuint max = unchecked((nuint)ulong.MaxValue);
#endif
        var ex = Record.Exception(() => NativeAllocator.Alloc(max - 10, backend));
        Assert.NotNull(ex);
        Assert.IsType<OutOfMemoryException>(ex);
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

    [Fact]
    public void ApplyProtection_NullOrZero_ShouldReturnEarly()
    {
        var exNull = Record.Exception(() => NativeAllocator.ApplyProtection(null, PageSize, MemoryProtectionMode.ReadOnly));
        Assert.Null(exNull);

        var ptr = NativeAllocator.Alloc(PageSize, NativeAllocatorBackend.PlatformInvoke);
        var exZero = Record.Exception(() => NativeAllocator.ApplyProtection(ptr, 0, MemoryProtectionMode.ReadOnly));
        Assert.Null(exZero);

        NativeAllocator.Free(ptr, NativeAllocatorBackend.PlatformInvoke);
    }

    [Fact]
    public void ApplyProtection_ValidPointer_ShouldSucceed()
    {
        var ptr = NativeAllocator.Alloc(PageSize, NativeAllocatorBackend.PlatformInvoke);
        Assert.NotEqual(IntPtr.Zero, (IntPtr)ptr);

        var span = new Span<byte>(ptr, (int)PageSize);
        span.Fill(0xAA);
        Assert.Equal(0xAA, span[0]);

        // Apply ReadOnly protection
        var exReadOnly = Record.Exception(() => NativeAllocator.ApplyProtection(ptr, PageSize, MemoryProtectionMode.ReadOnly));
        Assert.Null(exReadOnly);

        // Read access should still work
        Assert.Equal(0xAA, span[0]);

        // Restore to None (Read/Write)
        var exNone = Record.Exception(() => NativeAllocator.ApplyProtection(ptr, PageSize, MemoryProtectionMode.None));
        Assert.Null(exNone);

        // Write access should work again
        span.Fill(0xBB);
        Assert.Equal(0xBB, span[0]);

        NativeAllocator.Free(ptr, NativeAllocatorBackend.PlatformInvoke);
    }

    [Fact]
    public void ApplyProtection_SubSpan_ShouldWork()
    {
        // Allocate 2 pages
        var ptr = NativeAllocator.Alloc(PageSize * 2, NativeAllocatorBackend.PlatformInvoke);
        var span = new Span<byte>(ptr, (int)(PageSize * 2));
        span.Fill(0xCC);

        // Protect only the second page
        var secondPagePtr = (void*)((byte*)ptr + PageSize);
        var exSub = Record.Exception(() => NativeAllocator.ApplyProtection(secondPagePtr, PageSize, MemoryProtectionMode.ReadOnly));
        Assert.Null(exSub);

        // First page is still writable
        span[0] = 0xDD;
        Assert.Equal(0xDD, span[0]);
        // Second page is readable
        Assert.Equal(0xCC, span[(int)PageSize]);

        // Restore protection
        NativeAllocator.ApplyProtection(ptr, PageSize * 2, MemoryProtectionMode.None);
        NativeAllocator.Free(ptr, NativeAllocatorBackend.PlatformInvoke);
    }

    [Fact]
    public void ApplyProtection_ReadOnly_ShouldPreventWrite()
    {
        var envVar = "SHARPARENA_TEST_CRASH";
        if (Environment.GetEnvironmentVariable(envVar) == "1")
        {
            var ptr = NativeAllocator.Alloc(PageSize, NativeAllocatorBackend.PlatformInvoke);
            NativeAllocator.ApplyProtection(ptr, PageSize, MemoryProtectionMode.ReadOnly);
            // This should cause a native crash (e.g. AccessViolationException or Segmentation fault)
            *(byte*)ptr = 0xFF;
            return;
        }

        var assemblyPath = typeof(NativeAllocatorTests).Assembly.Location;

        var processStartInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"test \"{assemblyPath}\" --no-build --no-restore --filter ApplyProtection_ReadOnly_ShouldPreventWrite",
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        processStartInfo.EnvironmentVariables[envVar] = "1";

        using var process = System.Diagnostics.Process.Start(processStartInfo);
        Assert.NotNull(process);

        process.WaitForExit();

        // We expect the child test process to crash, which results in a non-zero exit code.
        // Since different platforms represent unmanaged memory access violations differently
        // (AccessViolationException on Windows vs native crash on Linux/macOS),
        // asserting a non-zero exit code is the most portable and robust verification.
        Assert.NotEqual(0, process.ExitCode);
    }
}
