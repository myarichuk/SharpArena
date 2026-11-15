using System;
using FluentAssertions;
using SharpArena.Allocators;
using SharpArena.Collections;
using Xunit;

namespace SharpArena.Tests.Regression;

public unsafe class ReviewRegressionTests2
{
#if NET8_0_OR_GREATER
    [Fact]
    public void ArenaBlockList_GetSpan_RespectsNaturalAlignment()
    {
        using var arena = new ArenaAllocator();
        var list = new ArenaBlockList<System.Runtime.Intrinsics.Vector128<float>>(arena, 2);
        for (int i = 0; i < 5; i++)
        {
            list.Add(System.Runtime.Intrinsics.Vector128.Create((float)i));
        }

        arena.Alloc(1); // perturb alignment

        var span = list.GetSpan();
        span.Length.Should().Be(5);
        var addr = (nuint)System.Runtime.CompilerServices.Unsafe.AsPointer(
            ref System.Runtime.InteropServices.MemoryMarshal.GetReference(span));
        (addr % 16).Should().Be(0);
        for (int i = 0; i < 5; i++)
        {
            System.Runtime.Intrinsics.Vector128.GetElement(span[i], 0).Should().Be(i);
        }
    }
#endif

    [Fact]
    public void ArenaPtrStack_Grow_ResizesInPlace()
    {
        using var arena = new ArenaAllocator();
        var stack = new ArenaPtrStack<int>(arena, 2);
        int a = 1, b = 2, c = 3;
        stack.Push(&a);
        stack.Push(&b);

        var before = arena.AllocatedBytes;
        int oldCap = stack.Capacity;
        stack.Push(&c);
        int newCap = stack.Capacity;

        newCap.Should().BeGreaterThan(oldCap);
        (arena.AllocatedBytes - before).Should().Be((nuint)((newCap - oldCap) * sizeof(int*)));

        ((nuint)stack.Pop()).Should().Be((nuint)(&c));
        ((nuint)stack.Pop()).Should().Be((nuint)(&b));
        ((nuint)stack.Pop()).Should().Be((nuint)(&a));
    }

    [Theory]
    [InlineData("hello", "hello", true)]
    [InlineData("hello", "hellp", false)]
    [InlineData("hello", "hell", false)]
    [InlineData("hell", "hello", false)]
    [InlineData("héllo", "héllo", true)]
    [InlineData("héllo", "hello", false)]
    [InlineData("hello", "héllo", false)]
    [InlineData("", "", true)]
    [InlineData("", "x", false)]
    [InlineData("x", "", false)]
    public void Utf8_EqualsString(string stored, string other, bool expected)
    {
        using var arena = new ArenaAllocator();
        var s = ArenaUtf8String.Clone(stored, arena);
        s.Equals(other).Should().Be(expected);
    }

    [Fact]
    public void Utf8_EqualsNull_MatchesEmpty()
    {
        using var arena = new ArenaAllocator();
        ArenaUtf8String.Clone("", arena).Equals((string?)null).Should().BeTrue();
        ArenaUtf8String.Clone("a", arena).Equals((string?)null).Should().BeFalse();
    }
}
