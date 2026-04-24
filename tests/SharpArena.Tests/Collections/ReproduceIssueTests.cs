using SharpArena.Allocators;
using SharpArena.Collections;
using Xunit;
using System.Linq;
using FluentAssertions;

namespace SharpArena.Tests.Collections;

public unsafe class ReproduceIssueTests : IDisposable
{
    private readonly ArenaAllocator _arena = new();

    public void Dispose() => _arena.Dispose();

    [Fact]
    public void ArenaList_Of_ArenaString_ShouldCompileAndWork()
    {
        // This test confirms that ArenaString satisfies the 'unmanaged' constraint
        // required by ArenaList<T>.
        var list = new ArenaList<ArenaString>(_arena);
        
        var s1 = ArenaString.Clone("Hello", _arena);
        var s2 = ArenaString.Clone("World", _arena);
        
        list.Add(s1);
        list.Add(s2);
        
        list.Length.Should().Be(2);
        list[0].ToString().Should().Be("Hello");
        list[1].ToString().Should().Be("World");

        list.Contains(s1).Should().BeTrue();
        
        list.RemoveAt(0);
        list.Length.Should().Be(1);
        list[0].ToString().Should().Be("World");
    }

    [Fact]
    public void ArenaBlockList_Of_ArenaString_ShouldCompileAndWork()
    {
        var list = new ArenaBlockList<ArenaString>(_arena);

        var s1 = ArenaString.Clone("Hello", _arena);
        var s2 = ArenaString.Clone("World", _arena);

        list.Add(s1);
        list.Add(s2);

        list.Count.Should().Be((nuint)2);
        var items = list.ToList();
        items[0].ToString().Should().Be("Hello");
        items[1].ToString().Should().Be("World");
    }

    [Fact]
    public void ArenaPtrStack_Of_ArenaString_ShouldCompileAndWork()
    {
        var stack = new ArenaPtrStack<ArenaString>(_arena);

        var s1 = ArenaString.Clone("Hello", _arena);
        var s2 = ArenaString.Clone("World", _arena);

        stack.Push(&s1);
        stack.Push(&s2);

        stack.Count.Should().Be(2);
        stack.Pop()->ToString().Should().Be("World");
        stack.Pop()->ToString().Should().Be("Hello");
    }
}
