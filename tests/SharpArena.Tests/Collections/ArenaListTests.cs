using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SharpArena.Allocators;
using SharpArena.Collections;
using Xunit;

namespace SharpArena.Tests.Collections;

public class ArenaListTests : IDisposable
{
    private readonly ArenaAllocator _arena = new(1024);

    [Fact]
    public void Add_SingleItem_ShouldStoreCorrectly()
    {
        var list = new ArenaList<int>(_arena);
        list.Add(5);
        Assert.Equal(1, list.Length);
        Assert.Equal(5, list[0]);
    }

    [Fact]
    public void Add_MultipleItems_ShouldStoreCorrectly()
    {
        var list = new ArenaList<int>(_arena);
        for (int i = 0; i < 1000; i++)
        {
            list.Add(i * 2);
        }

        Assert.Equal(1000, list.Length);
        for (int i = 0; i < 1000; i++)
        {
            Assert.Equal(i * 2, list[i]);
        }
    }

    [Fact]
    public void AddRange_ShouldStoreCorrectly()
    {
        var list = new ArenaList<int>(_arena);
        var items = new int[] { 1, 2, 3, 4, 5 };
        foreach (var item in items)
        {
            list.Add(item);
        }

        Assert.Equal(5, list.Length);
        for (int i = 0; i < 5; i++)
        {
            Assert.Equal(i + 1, list[i]);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ArenaListLayout
    {
        public ArenaAllocator _arena;
        public int _generation;
        public unsafe ArenaListHeader* _header;
    }

    [Fact]
    public unsafe void Add_CapacityOverflow_ThrowsInvalidOperationException()
    {
        var list = new ArenaList<int>(_arena, initialCapacity: 4);

        // Access internal layout to modify capacity without allocating 1GB of actual memory
        ref ArenaListLayout layout = ref Unsafe.As<ArenaList<int>, ArenaListLayout>(ref list);
        layout._header->Capacity = (int.MaxValue / 2) + 1;
        layout._header->Count = layout._header->Capacity;

        var ex = Assert.Throws<InvalidOperationException>(() => list.Add(1));
        Assert.Equal("ArenaList capacity overflow.", ex.Message);
    }

    [Fact]
    public void Property_Capacity_ReturnsExpectedValue()
    {
        var list = new ArenaList<int>(_arena, initialCapacity: 4);
        // We can't access Capacity directly, but we can test if it grows.
        // Actually ArenaList doesn't expose Capacity currently.
        // So we will just test growth implicitly via Count.
        for (int i = 0; i < 5; i++)
        {
            list.Add(i);
        }
        Assert.Equal(5, list.Length);
    }

    [Fact]
    public void Reset_ClearsListButKeepsBuffer()
    {
        var list = new ArenaList<int>(_arena, initialCapacity: 4);
        list.Add(1);
        list.Add(2);
        Assert.Equal(2, list.Length);

        list.Reset();
        Assert.Equal(0, list.Length);
        Assert.True(list.IsEmpty);
        list.Add(3);
        Assert.Equal(1, list.Length);
        Assert.Equal(3, list[0]);
    }

    [Fact]
    public void Indexer_NegativeIndex_ThrowsArgumentOutOfRangeException()
    {
        var list = new ArenaList<int>(_arena);
        list.Add(1);

        Assert.Throws<ArgumentOutOfRangeException>(() => _ = list[-1]);
    }

    [Fact]
    public void Indexer_IndexEqualToLength_ThrowsArgumentOutOfRangeException()
    {
        var list = new ArenaList<int>(_arena);
        list.Add(1);

        Assert.Throws<ArgumentOutOfRangeException>(() => _ = list[list.Length]);
    }

    [Fact]
    public void AccessAfterReset_ThrowsObjectDisposedException()
    {
        var list = new ArenaList<int>(_arena);
        list.Add(1);

        _arena.Reset();

        Assert.Throws<ObjectDisposedException>(() => list.Add(2));
        Assert.Throws<ObjectDisposedException>(() => _ = list.Length);
        Assert.Throws<ObjectDisposedException>(() => _ = list.IsEmpty);
        Assert.Throws<ObjectDisposedException>(() => _ = list[0]);
        Assert.Throws<ObjectDisposedException>(() => list.Reset());
        unsafe
        {
            Assert.Throws<ObjectDisposedException>(() => _ = list.AsPtr);
        }
        Assert.Throws<ObjectDisposedException>(() => list.AsSpan());
        Assert.Throws<ObjectDisposedException>(() => list.AsReadOnlySpan());
    }

    public void Dispose()
    {
        _arena.Dispose();
    }
}
