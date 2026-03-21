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

    [Fact]
    public void AccessAfterReset_ThrowsObjectDisposedException()
    {
        var list = new ArenaBlockList<int>(_arena);
        list.Add(1);

        _arena.Reset();

        Assert.Throws<ObjectDisposedException>(() => list.Add(2));
        Assert.Throws<ObjectDisposedException>(() => _ = list.Count);
        Assert.Throws<ObjectDisposedException>(() => _ = list.Capacity);
        Assert.Throws<ObjectDisposedException>(() => list.GetEnumerator());
        Assert.Throws<ObjectDisposedException>(() => list.GetSpan());
        Assert.Throws<ObjectDisposedException>(() => list.Reset());
    }

    [Fact]
    public void CreateBlock_WithOverflowCapacity_ThrowsOutOfMemoryException()
    {
#if NET7_0_OR_GREATER
        nuint max = nuint.MaxValue;
#else
        nuint max = unchecked((nuint)ulong.MaxValue);
#endif
        // Set capacity to a value that would cause capacity * sizeof(int) to overflow.
        nuint overflowCapacity = (max / (nuint)sizeof(int)) + 1;

        Assert.Throws<OutOfMemoryException>(() => new ArenaBlockList<int>(_arena, blockSize: overflowCapacity));
    }
    [Fact]
    public void StructCopy_SharesState()
    {
        var list1 = new ArenaBlockList<int>(_arena, blockSize: 2);
        list1.Add(1);
        
        // Copy by value
        var list2 = list1;
        
        list2.Add(2);
        
        // Both should see the same count and elements
        Assert.Equal((nuint)2, list1.Count);
        Assert.Equal((nuint)2, list2.Count);
        
        // Add more to trigger growth in list2
        list2.Add(3); // Should create a new block
        
        Assert.Equal((nuint)3, list1.Count);
        Assert.Equal((nuint)3, list2.Count);
        
        var elements1 = list1.ToArray();
        var elements2 = list2.ToArray();
        
        Assert.Equal(new[] { 1, 2, 3 }, elements1);
        Assert.Equal(new[] { 1, 2, 3 }, elements2);
    }

    [Fact]
    public void GetSpan_EmptyList_ReturnsEmptySpan()
    {
        var list = new ArenaBlockList<int>(_arena);
        var span = list.GetSpan();
        Assert.True(span.IsEmpty);
        Assert.Equal(0, span.Length);
    }

    [Fact]
    public void GetSpan_SingleBlock_ReturnsCorrectSpan()
    {
        var list = new ArenaBlockList<int>(_arena, blockSize: 10);
        for (int i = 0; i < 5; i++)
        {
            list.Add(i);
        }

        var span = list.GetSpan();
        Assert.Equal(5, span.Length);
        for (int i = 0; i < 5; i++)
        {
            Assert.Equal(i, span[i]);
        }
    }

    [Fact]
    public void GetSpan_MultipleBlocks_ReturnsContiguousSpan()
    {
        // Use a small block size to force multiple blocks
        var list = new ArenaBlockList<int>(_arena, blockSize: 2);

        // This will create blocks of size 2, 4, 8, 16...
        // 2 + 4 + 8 = 14. Adding 15 elements should use at least 3-4 blocks.
        for (int i = 0; i < 15; i++)
        {
            list.Add(i);
        }

        var span = list.GetSpan();
        Assert.Equal(15, span.Length);

        for (int i = 0; i < 15; i++)
        {
            Assert.Equal(i, span[i]);
        }

        // Verify it is indeed contiguous and correctly copied from multiple blocks
        var expected = Enumerable.Range(0, 15).ToArray();
        Assert.True(span.SequenceEqual(expected));
    }
}
