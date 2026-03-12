using SharpArena.Allocators;
using SharpArena.Helpers;
using Xunit;

namespace SharpArena.Tests.Helpers;

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

    public void Dispose()
    {
        _arena.Dispose();
    }
}
