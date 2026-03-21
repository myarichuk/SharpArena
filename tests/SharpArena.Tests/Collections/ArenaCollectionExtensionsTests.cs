using System;
using SharpArena.Allocators;
using SharpArena.Collections;
using Xunit;

namespace SharpArena.Tests.Collections;

public class ArenaCollectionExtensionsTests : IDisposable
{
    private readonly ArenaAllocator _arena = new(1024);

    public void Dispose()
    {
        _arena.Dispose();
    }

    [Fact]
    public void ToArenaList_NullArena_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => ReadOnlySpan<int>.Empty.ToArenaList(null!));
    }

    [Fact]
    public void ToArenaList_EmptyReadOnlySpan_ReturnsEmptyArenaList()
    {
        ReadOnlySpan<int> span = ReadOnlySpan<int>.Empty;
        var list = span.ToArenaList(_arena);

        Assert.True(list.IsEmpty);
        Assert.Equal(0, list.Length);
    }

    [Fact]
    public void ToArenaList_PopulatedReadOnlySpan_ReturnsPopulatedArenaList()
    {
        ReadOnlySpan<int> span = stackalloc int[] { 10, 20, 30, 40, 50 };
        var list = span.ToArenaList(_arena);

        Assert.False(list.IsEmpty);
        Assert.Equal(5, list.Length);

        for (int i = 0; i < 5; i++)
        {
            Assert.Equal(span[i], list[i]);
        }
    }

    [Fact]
    public void ToArenaList_EmptySpan_ReturnsEmptyArenaList()
    {
        Span<int> span = Span<int>.Empty;
        var list = span.ToArenaList(_arena);

        Assert.True(list.IsEmpty);
        Assert.Equal(0, list.Length);
    }

    [Fact]
    public void ToArenaList_PopulatedSpan_ReturnsPopulatedArenaList()
    {
        Span<int> span = stackalloc int[] { 100, 200, 300 };
        var list = span.ToArenaList(_arena);

        Assert.False(list.IsEmpty);
        Assert.Equal(3, list.Length);

        for (int i = 0; i < 3; i++)
        {
            Assert.Equal(span[i], list[i]);
        }
    }
}
