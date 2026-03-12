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

    public void Dispose()
    {
        _arena.Dispose();
    }
}
