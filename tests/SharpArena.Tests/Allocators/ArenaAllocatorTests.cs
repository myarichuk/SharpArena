using System.Threading;
using System.Threading.Tasks;
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
    public async Task Reset_ConcurrentWithAlloc_ShouldNotThrowOrCorruptState()
    {
        using var arena = new ArenaAllocator(4096);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));

        var allocTask = Task.Run(() =>
        {
            while (!cts.IsCancellationRequested)
            {
                var p = (byte*)arena.Alloc(32, 8);
                *p = 0x5A;
            }
        }, cts.Token);

        var resetTask = Task.Run(() =>
        {
            while (!cts.IsCancellationRequested)
            {
                arena.Reset();
            }
        }, cts.Token);

        await Task.WhenAll(allocTask, resetTask);

        var check = arena.Alloc(64, 8);
        Assert.NotEqual(IntPtr.Zero, (nint)check);
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
}
