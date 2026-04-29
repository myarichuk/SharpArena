using FluentAssertions;
using SharpArena.Allocators;
using SharpArena.Collections;
using Xunit;

namespace SharpArena.Tests.Collections;

public class ArenaSetTests : IDisposable
{
    private readonly ArenaAllocator _arena = new();

    public void Dispose() => _arena.Dispose();

    [Fact]
    public void Add_NewItem_ReturnsTrueAndIncrementsCount()
    {
        var set = new ArenaSet<int>(_arena);
        set.Add(1).Should().BeTrue();
        set.Count.Should().Be(1);
        set.Contains(1).Should().BeTrue();
    }

    [Fact]
    public void Add_DuplicateItem_ReturnsFalse()
    {
        var set = new ArenaSet<int>(_arena);
        set.Add(1).Should().BeTrue();
        set.Add(1).Should().BeFalse();
        set.Count.Should().Be(1);
    }

    [Fact]
    public void Add_ManyItems_CausesGrowth()
    {
        var set = new ArenaSet<int>(_arena, initialCapacity: 4);
        for (int i = 0; i < 100; i++)
        {
            set.Add(i).Should().BeTrue();
        }

        set.Count.Should().Be(100);
        for (int i = 0; i < 100; i++)
        {
            set.Contains(i).Should().BeTrue();
        }
    }

    [Fact]
    public void Contains_NonExistentItem_ReturnsFalse()
    {
        var set = new ArenaSet<int>(_arena);
        set.Add(1);
        set.Contains(2).Should().BeFalse();
    }

    [Fact]
    public void Clear_ResetsCountAndClearsContains()
    {
        var set = new ArenaSet<int>(_arena);
        set.Add(1);
        set.Add(2);
        
        set.Clear();
        
        set.Count.Should().Be(0);
        set.Contains(1).Should().BeFalse();
        set.Contains(2).Should().BeFalse();
    }

    [Fact]
    public void Remove_ThrowsNotSupportedException()
    {
        var set = new ArenaSet<int>(_arena);
        set.Add(1);
        Action act = () => set.Remove(1);
        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void CopyTo_CopiesAllItems()
    {
        var set = new ArenaSet<int>(_arena);
        set.Add(1);
        set.Add(2);
        set.Add(3);

        int[] array = new int[3];
        set.CopyTo(array, 0);

        array.Should().Contain(new[] { 1, 2, 3 });
    }

    [Fact]
    public void UnionWith_AddsNewItems()
    {
        var set = new ArenaSet<int>(_arena);
        set.Add(1);
        
        set.UnionWith(new[] { 1, 2, 3 });
        
        set.Count.Should().Be(3);
        set.Should().Contain(new[] { 1, 2, 3 });
    }

    [Fact]
    public void SetEquals_ReturnsTrueIfIdentical()
    {
        var set = new ArenaSet<int>(_arena);
        set.Add(1);
        set.Add(2);

        set.SetEquals(new[] { 2, 1 }).Should().BeTrue();
        set.SetEquals(new[] { 1, 2, 3 }).Should().BeFalse();
        set.SetEquals(new[] { 1 }).Should().BeFalse();
    }

    [Fact]
    public void ArenaUtf16String_WorksInSet()
    {
        var set = new ArenaSet<ArenaUtf16String>(_arena);
        var s1 = ArenaUtf16String.Clone("hello", _arena);
        var s2 = ArenaUtf16String.Clone("world", _arena);
        var s3 = ArenaUtf16String.Clone("hello", _arena);

        set.Add(s1).Should().BeTrue();
        set.Add(s2).Should().BeTrue();
        set.Add(s3).Should().BeFalse(); // Content equality

        set.Count.Should().Be(2);
        set.Contains(s1).Should().BeTrue();
        set.Contains(s3).Should().BeTrue();
    }

    [Fact]
    public void Slice_PreservesGeneration_ValidationSucceeds()
    {
        var text = "Hello, World!";
        var str = ArenaUtf16String.Clone(text, _arena);
        var slice = str.Slice(7, 5);

        // Before the fix, this would throw because Slice lost the generation (became 0)
        slice.Verify(_arena); 
        slice.IsAlive(_arena).Should().BeTrue();
        slice.ToString().Should().Be("World");
    }
}
