using SharpArena.Allocators;
using SharpArena.Collections;
using Xunit;

namespace SharpArena.Tests.Collections;

public unsafe class ArenaPtrStackTests
{
    [Fact]
    public void PushAndPop_ShouldReturnInLIFOOrder()
    {
        using var arena = new ArenaAllocator(4096);
        var stack = new ArenaPtrStack<int>(arena);

        var a = (int*)arena.Alloc(sizeof(int));
        *a = 10;
        var b = (int*)arena.Alloc(sizeof(int));
        *b = 20;
        var c = (int*)arena.Alloc(sizeof(int));
        *c = 30;

        stack.Push(a);
        stack.Push(b);
        stack.Push(c);

        Assert.Equal(3, stack.Count);
        Assert.False(stack.IsEmpty);

        Assert.Equal(30, *stack.Pop());
        Assert.Equal(20, *stack.Pop());
        Assert.Equal(10, *stack.Pop());

        Assert.True(stack.IsEmpty);
    }

    [Fact]
    public void Peek_ShouldReturnTopWithoutRemoving()
    {
        using var arena = new ArenaAllocator(4096);
        var stack = new ArenaPtrStack<int>(arena);

        var x = (int*)arena.Alloc(sizeof(int));
        *x = 1;
        var y = (int*)arena.Alloc(sizeof(int));
        *y = 2;

        stack.Push(x);
        stack.Push(y);

        var top = stack.Peek();
        Assert.Equal(2, *top);
        Assert.Equal(2, stack.Count);

        // Modify via pointer
        *top = 99;
        Assert.Equal(99, *stack.Peek());
    }

    [Fact]
    public void Clear_ShouldResetCount()
    {
        using var arena = new ArenaAllocator(4096);
        var stack = new ArenaPtrStack<int>(arena);

        for (int i = 0; i < 3; i++)
        {
            var ptr = (int*)arena.Alloc(sizeof(int));
            *ptr = i;
            stack.Push(ptr);
        }

        stack.Clear();

        Assert.True(stack.IsEmpty);
        Assert.Equal(0, stack.Count);

        var newPtr = (int*)arena.Alloc(sizeof(int));
        *newPtr = 42;
        stack.Push(newPtr);

        Assert.Equal(1, stack.Count);
        Assert.Equal(42, *stack.Peek());
    }

    [Fact]
    public void Pop_OnEmpty_ShouldThrow()
    {
        using var arena = new ArenaAllocator(4096);
        var stack = new ArenaPtrStack<int>(arena);

        Assert.Throws<InvalidOperationException>(() => stack.Pop());
    }

    [Fact]
    public void Peek_OnEmpty_ShouldThrow()
    {
        using var arena = new ArenaAllocator(4096);
        var stack = new ArenaPtrStack<int>(arena);

        Assert.Throws<InvalidOperationException>(() => stack.Peek());
    }

    [Fact]
    public void PushBeyondInitialCapacity_ShouldGrow()
    {
        using var arena = new ArenaAllocator(4096);
        var stack = new ArenaPtrStack<int>(arena, initialCapacity: 2);

        for (int i = 0; i < 10; i++)
        {
            var ptr = (int*)arena.Alloc(sizeof(int));
            *ptr = i;
            stack.Push(ptr);
        }

        Assert.Equal(10, stack.Count);

        for (int i = 9; i >= 0; i--)
        {
            Assert.Equal(i, *stack.Pop());
        }

        Assert.True(stack.IsEmpty);
    }

    [Fact]
    public void CanHandleUnmanagedStruct()
    {
        using var arena = new ArenaAllocator(4096);
        var stack = new ArenaPtrStack<Foobar>(arena, initialCapacity: 4);

        var f1 = (Foobar*)arena.Alloc((uint)sizeof(Foobar));
        *f1 = new Foobar { X = 1, Y = 2 };

        var f2 = (Foobar*)arena.Alloc((uint)sizeof(Foobar));
        *f2 = new Foobar { X = 3, Y = 4 };

        stack.Push(f1);
        stack.Push(f2);

        var popped = stack.Pop();
        Assert.Equal(3, popped->X);
        Assert.Equal(4, popped->Y);
    }

    [Fact]
    public void CopyingStack_ShouldShareHeader()
    {
        using var arena = new ArenaAllocator(4096);
        var original = new ArenaPtrStack<int>(arena);

        var a = (int*)arena.Alloc(sizeof(int));
        *a = 1;
        original.Push(a);

        // Copy the struct
        var copy = original;

        // Push via copy, pop via original
        var b = (int*)arena.Alloc(sizeof(int));
        *b = 2;
        copy.Push(b);

        Assert.Equal(2, original.Count); // Both see the same header
        Assert.Equal(2, *original.Peek());
        Assert.Equal(2, *copy.Peek());

        // Pop via one should reflect in the other
        _ = original.Pop();
        Assert.Equal(1, copy.Count);
        Assert.Equal(1, *copy.Peek());
    }

    [Fact]
    public void StackInsideStruct_ShouldRetainSharedState()
    {
        using var arena = new ArenaAllocator(4096);
        var container1 = new Container { Stack = new ArenaPtrStack<int>(arena) };

        var val1 = (int*)arena.Alloc(sizeof(int));
        *val1 = 42;
        container1.Stack.Push(val1);

        var container2 = container1; // copy the whole struct

        var val2 = (int*)arena.Alloc(sizeof(int));
        *val2 = 84;
        container2.Stack.Push(val2);

        Assert.Equal(2, container1.Stack.Count);
        Assert.Equal(84, *container1.Stack.Peek());
    }

    [Fact]
    public void ShouldHandleLargeNumberOfPushes()
    {
        using var arena = new ArenaAllocator(1024);
        var stack = new ArenaPtrStack<int>(arena, 4);

        const int count = 10_000;
        for (int i = 0; i < count; i++)
        {
            var ptr = (int*)arena.Alloc(sizeof(int));
            *ptr = i;
            stack.Push(ptr);
        }

        Assert.Equal(count, stack.Count);
        for (int i = count - 1; i >= 0; i--)
        {
            Assert.Equal(i, *stack.Pop());
        }
    }

    [Fact]
    public void Growth_ShouldPreserveSharedHeader()
    {
        using var arena = new ArenaAllocator(4096);
        var s1 = new ArenaPtrStack<int>(arena, 1);
        var s2 = s1;

        for (int i = 0; i < 10; i++)
        {
            var p = (int*)arena.Alloc(sizeof(int));
            *p = i;
            s1.Push(p);
        }

        Assert.Equal(10, s2.Count);
        Assert.Equal(9, *s2.Peek());
    }

    private struct Container
    {
        public ArenaPtrStack<int> Stack;
    }

    private struct Foobar
    {
        public int X;
        public int Y;
    }

    [Fact]
    public void Pop_EmptyStack_ThrowsInvalidOperationException()
    {
        using var _arena = new ArenaAllocator(4096);
        var stack = new ArenaPtrStack<int>(_arena, 4);
        Assert.Throws<InvalidOperationException>(() => stack.Pop());
    }

    [Fact]
    public void Peek_EmptyStack_ThrowsInvalidOperationException()
    {
        using var _arena = new ArenaAllocator(4096);
        var stack = new ArenaPtrStack<int>(_arena, 4);
        Assert.Throws<InvalidOperationException>(() => stack.Peek());
    }

    [Fact]
    public void Property_Capacity_ReturnsExpectedValue()
    {
        using var _arena = new ArenaAllocator(4096);
        var stack = new ArenaPtrStack<int>(_arena, 4);
        Assert.Equal(4, stack.Capacity);

        int dummy = 42;
        for (int i = 0; i < 5; i++)
        {
            stack.Push(&dummy);
        }

        Assert.True(stack.Capacity >= 5);
    }
}
