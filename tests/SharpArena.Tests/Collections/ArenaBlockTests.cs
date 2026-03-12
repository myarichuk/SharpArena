using SharpArena.Allocators;
using SharpArena.Collections;
using Xunit;

namespace SharpArena.Tests.Collections;

public unsafe class ArenaBlockListTests : IDisposable
{
    private readonly ArenaAllocator _arena = new();

    public void Dispose() => _arena.Dispose();

    [Fact]
    public void Add_SingleValue_DoesNotCrash()
    {
        _ = new ArenaBlockList<int>(_arena) { 123 };
    }

    [Fact]
    public void Add_MultipleValues_DoesNotCrash_AndSupportsExpansion()
    {
        var list = new ArenaBlockList<int>(_arena, blockSize: 2);

        // Add enough to force multiple blocks
        for (int i = 0; i < 100; i++)
        {
            list.Add(i);
        }

        // If we got here, no segfaults, stack corruption, or allocator errors
        // For safety, allocate again to ensure arena is still consistent
        var ptr = _arena.Alloc(8);
        Assert.NotEqual(0, (nint)ptr);
    }

    [Fact]
    public void Add_DifferentTypes_BehavesConsistently()
    {
        var list = new ArenaBlockList<byte>(_arena, blockSize: 4);
        for (int i = 0; i < 50; i++)
        {
            list.Add((byte)i);
        }

        var list2 = new ArenaBlockList<long>(_arena, blockSize: 4);
        for (long i = 0; i < 50; i++)
        {
            list2.Add(i);
        }

        // No exceptions, no corruption, allocator still usable
        var check = _arena.Alloc(16);
        Assert.NotEqual(0, (nint)check);
    }

    [Fact]
    public void StressTest_Add_ThousandBlocks()
    {
        var list = new ArenaBlockList<int>(_arena, blockSize: 1);

        for (int i = 0; i < 10_000; i++)
        {
            list.Add(i);
        }

        // Arena should still allocate new memory afterwards
        var p = _arena.Alloc(64);
        Assert.NotEqual(0, (nint)p);
    }

    [Fact]
    public void Arena_Reset_AllowsReallocation()
    {
        var list = new ArenaBlockList<int>(_arena, blockSize: 4);

        for (int i = 0; i < 100; i++)
        {
            list.Add(i);
        }

        _arena.Reset();

        // Allocating again after reset should not throw
        var list2 = new ArenaBlockList<int>(_arena, blockSize: 4);
        list2.Add(42);
    }

    [Fact]
    public void Enumerate_ReturnsAllAddedElements()
    {
        var list = new ArenaBlockList<int>(_arena, blockSize: 2);

        for (int i = 0; i < 10; i++)
        {
            list.Add(i);
        }

        var result = list.ToArray();

        Assert.Equal(Enumerable.Range(0, 10), result);
        Assert.Equal((nuint)10, list.Count);
    }
}
