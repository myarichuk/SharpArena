using SharpArena.Allocators;
using Xunit;

namespace SharpArena.Tests.Allocators;

[CollectionDefinition("ArenaAllocatorTests", DisableParallelization = true)]
public class NonConcurrentCollection
{
}

[Collection("ArenaAllocatorTests")]
public unsafe class ArenaAllocatorTests : IDisposable
{
    private readonly ArenaAllocator _arena = new();

    public void Dispose() => _arena.Dispose();

    [Fact]
    public void Alloc_SmallBlockWithinDefaultSegment_ShouldReturnValidPointer()
    {
        var ptr = _arena.Alloc(128);
        Assert.NotEqual(IntPtr.Zero, (nint)ptr);
    }

    [Fact]
    public void Alloc_MultipleSmallAllocations_ShouldNotOverlap()
    {
        var a = (byte*)_arena.Alloc(64);
        var b = (byte*)_arena.Alloc(64);

        Assert.True(b > a, "Second allocation should be at a higher address.");
        Assert.True((nuint)(b - a) >= 64);
    }

    [Theory]
    [InlineData(32u)]
    [InlineData(64u)]
    public void Alloc_WithAlignment_ShouldReturnAlignedPointer(uint align)
    {
        var alignment = (nuint)align;
        var ptr = _arena.Alloc(16, alignment);

        Assert.NotEqual(IntPtr.Zero, (nint)ptr);
        Assert.True(((nuint)ptr & (alignment - 1)) == 0, "Pointer should respect requested alignment.");
    }

    [Fact]
    public void Alloc_LargeAllocation_ShouldTriggerNewSegment()
    {
        // default segment = 10MB, allocate 20MB to force growth
        var large = (byte*)_arena.Alloc(20 * 1024 * 1024);
        var small = (byte*)_arena.Alloc(128);

        // large allocation must be from earlier segment
        Assert.True(small != large);
    }

    [Fact]
    public void Dispose_ShouldFreeAllSegmentsWithoutCrash()
    {
        ArenaAllocator? arena = null;
        try
        {
            arena = new ArenaAllocator();
            arena.Alloc(4096);
            arena.Alloc(20 * 1024 * 1024); // force extra segment
        }
        finally
        {
            arena?.Dispose();
        }

        // double dispose should be safe
        arena.Dispose();
    }

    [Fact]
    public void AllocateManySmallObjects_ShouldNotThrowOrLeak()
    {
        for (int i = 0; i < 10_000; i++)
        {
            _arena.Alloc(64);
        }
    }

    [Fact]
    public void Arena_ShouldHandleExactSegmentFilling()
    {
        var total = 0u;
        var segSize = 1024 * 1024u; // small segment
        using var arena = new ArenaAllocator(segSize);

        while (true)
        {
            var ptr = arena.Alloc(256);
            total += 256;
            if (total > segSize)
            {
                break;
            }
        }
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var arena = new ArenaAllocator(4096);
        var p = arena.Alloc(256);
        arena.Dispose();
        arena.Dispose(); // should be a no-op, not crash
    }

    [Fact]
    public void Finalizer_DoesNotDoubleFree()
    {
        WeakReference? wr = null;
        new Action(() =>
        {
            var arena = new ArenaAllocator(4096);
            arena.Alloc(256);
            arena.Dispose();              // explicit
            wr = new WeakReference(arena);
        })();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();                     // should not crash; finalizer must be suppressed or no-op
        Assert.False(wr?.IsAlive);
    }

    [Fact]
    public void Alloc_ExceedingMaxSegmentSize_ShouldAllocateSuccessfully()
    {
        // initialSize: 512, maxSize: 1024
        using var arena = new ArenaAllocator(512, 1024);

        // Allocate larger than maxSize
        var ptr = arena.Alloc(4096);
        Assert.NotEqual(IntPtr.Zero, (nint)ptr);

        // Verify we can still allocate small things afterwards
        var smallPtr = arena.Alloc(64);
        Assert.NotEqual(IntPtr.Zero, (nint)smallPtr);
        Assert.NotEqual((nint)ptr, (nint)smallPtr);
    }

    [Fact]
    public void ConcurrentAlloc_And_Reset_ShouldNotCrash()
    {
        // Test beyond the basic _activeAllocations counter
        using var arena = new ArenaAllocator(4096);

        var options = new ParallelOptions { MaxDegreeOfParallelism = 4 };
        int completedAllocations = 0;

        Parallel.Invoke(options,
            () =>
            {
                for (int i = 0; i < 10000; i++)
                {
                    try
                    {
                        var ptr = arena.Alloc(16);
                        if (ptr != null)
                        {
                            Interlocked.Increment(ref completedAllocations);
                        }
                    }
                    catch (ObjectDisposedException)
                    {
                        // Dispose might have been called, but we are only testing Reset here
                    }
                }
            },
            () =>
            {
                for (int i = 0; i < 10000; i++)
                {
                    try
                    {
                        var ptr = arena.Alloc(32);
                        if (ptr != null)
                        {
                            Interlocked.Increment(ref completedAllocations);
                        }
                    }
                    catch (ObjectDisposedException)
                    {
                    }
                }
            },
            () =>
            {
                for (int i = 0; i < 100; i++)
                {
                    Thread.Sleep(1);
                    arena.Reset();
                }
            }
        );

        Assert.True(completedAllocations > 0);
    }

    [Theory]
    [InlineData(8u)]
    [InlineData(16u)]
    [InlineData(32u)]
    [InlineData(64u)]
    [InlineData(128u)]
    public void Alloc_WithVariousAlignments_ShouldAlignCorrectly(uint align)
    {
        var alignment = (nuint)align;
        // Allocate once to shift offset
        _arena.Alloc(1);

        var ptr = _arena.Alloc(16, alignment);

        Assert.NotEqual(IntPtr.Zero, (nint)ptr);
        Assert.True(((nuint)ptr & (alignment - 1)) == 0, $"Pointer should be aligned to at least {alignment}.");
    }

    [Fact]
    public void Alloc_ExtremeMemoryPressure_ShouldHandleGracefully()
    {
        nuint maxSize = 1024 * 1024; // 1MB
        using var arena = new ArenaAllocator(64 * 1024, maxSize);

        // Allocate 10x maxSize to trigger multiple segment creations at max size
        nuint totalAllocated = 0;
        nuint target = maxSize * 10;

        while (totalAllocated < target)
        {
            var ptr = arena.Alloc(16 * 1024); // 16KB chunks
            Assert.NotEqual(IntPtr.Zero, (nint)ptr);
            totalAllocated += 16 * 1024;
        }

        // We should have successfully allocated all memory
        Assert.True(totalAllocated >= target);
    }
}
