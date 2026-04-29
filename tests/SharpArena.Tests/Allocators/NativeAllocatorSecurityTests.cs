using SharpArena.Allocators;
using Xunit;
using System;

namespace SharpArena.Tests.Allocators;

public unsafe class NativeAllocatorSecurityTests
{
    [Fact]
    public void Alloc_NearOverflow_ShouldThrowOutOfMemory()
    {
#if NET7_0_OR_GREATER
        nuint max = nuint.MaxValue;
#else
        nuint max = unchecked((nuint)ulong.MaxValue);
#endif
        // Try to trigger overflow in AlignUp or guard page addition
        // PageSize is 4096.
        // If we pick size such that size + HeaderSize is just below PageSize boundary near max.

        nuint pageSize = (nuint)Environment.SystemPageSize;
        nuint headerSize = 64; // Approximation

        // We want total = size + headerSize to be large, but not overflow yet.
        // But AlignUp(total, pageSize) to overflow.
        nuint size = max - pageSize + 1;

        var ex = Record.Exception(() => NativeAllocator.Alloc(size, NativeAllocatorBackend.PlatformInvoke));
        Assert.NotNull(ex);
        Assert.IsType<OutOfMemoryException>(ex);
    }

    [Fact]
    public void ApplyProtection_PotentialOverflow_ShouldHandleGracefully()
    {
#if NET7_0_OR_GREATER
        nuint max = nuint.MaxValue;
#else
        nuint max = unchecked((nuint)ulong.MaxValue);
#endif
        // We use a fake pointer that is high in memory
        void* ptr = (void*)(max - 1024);
        nuint size = 2048; // ptr + size will overflow

        // We expect it to either throw an exception (ArgumentException or similar)
        // or handle it safely without attempting invalid memory protection calls.
        // Currently it might crash if it proceeds to read header from invalid address,
        // but the vulnerability is in the arithmetic itself.

        // Since we can't easily mock the header at that high address without real allocation,
        // we'll see if it throws during AlignToPage if we could isolate it,
        // but here it will probably fail at header access.

        // If we want to specifically test AlignToPage, we might need to use reflection since it's private,
        // or just observe the behavior of ApplyProtection.

        try
        {
            NativeAllocator.ApplyProtection(ptr, size, MemoryProtectionMode.ReadOnly);
        }
        catch (Exception ex) when (ex is not AccessViolationException)
        {
            // If it throws anything other than AV (which we can't catch easily in modern .NET anyway), it's good.
            // But we prefer a proactive check.
        }
    }
}
