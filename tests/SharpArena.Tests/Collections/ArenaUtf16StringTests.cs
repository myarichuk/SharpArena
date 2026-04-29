using FluentAssertions;
using SharpArena.Allocators;
using SharpArena.Collections;
using Xunit;

namespace SharpArena.Tests.Collections;

public unsafe class ArenaUtf16StringTests : IDisposable
{
    private readonly ArenaAllocator _arena = new();

    public void Dispose() => _arena.Dispose();

    [Fact]
    public void Clone_EmptySpan_ReturnsEmpty()
    {
        var result = ArenaUtf16String.Clone(ReadOnlySpan<char>.Empty, _arena);
        result.IsEmpty.Should().BeTrue();
        result.Length.Should().Be(0);
        result.ToString().Should().Be(string.Empty);
    }

    [Fact]
    public void Clone_ValidSpan_ReturnsArenaUtf16String()
    {
        var text = "Hello, World!";
        var result = ArenaUtf16String.Clone(text, _arena);

        result.IsEmpty.Should().BeFalse();
        result.Length.Should().Be(text.Length);
        result.ToString().Should().Be(text);
    }

    [Fact]
    public void Equals_ReadOnlySpan_ReturnsTrueIfEqual()
    {
        var text = "Test String";
        var result = ArenaUtf16String.Clone(text, _arena);

        result.Equals(text.AsSpan()).Should().BeTrue();
        result.Equals("Different".AsSpan()).Should().BeFalse();
    }

    [Fact]
    public void Equals_ArenaUtf16String_ReturnsTrueIfEqual()
    {
        var text = "Test String";
        var str1 = ArenaUtf16String.Clone(text, _arena);
        var str2 = ArenaUtf16String.Clone(text, _arena);
        var str3 = ArenaUtf16String.Clone("Different", _arena);

        str1.Equals(str2).Should().BeTrue();
        str1.Equals(str3).Should().BeFalse();
        (str1 == str2).Should().BeTrue();
        (str1 != str3).Should().BeTrue();
    }

    [Fact]
    public void Equals_Object_ReturnsTrueIfEqual()
    {
        var text = "Test String";
        var str1 = ArenaUtf16String.Clone(text, _arena);
        var str2 = ArenaUtf16String.Clone(text, _arena);

        str1.Equals((object)str2).Should().BeTrue();
        str1.Equals(new object()).Should().BeFalse();
    }

    [Fact]
    public void GetHashCode_ReturnsEqualForEqualContentAcrossClones()
    {
        var str1 = ArenaUtf16String.Clone("Test String", _arena);
        var str2 = ArenaUtf16String.Clone("Test String", _arena);

        str1.Equals(str2).Should().BeTrue();
        str1.GetHashCode().Should().Be(str2.GetHashCode());
    }

    [Fact]
    public void GetHashCode_ReturnsDifferentForDifferentContent()
    {
        var str1 = ArenaUtf16String.Clone("Test String", _arena);
        var str2 = ArenaUtf16String.Clone("Different", _arena);

        str1.Equals(str2).Should().BeFalse();
        str1.GetHashCode().Should().NotBe(str2.GetHashCode());
    }

    [Fact]
    public void Slice_ValidRange_ReturnsSubstring()
    {
        var text = "Hello, World!";
        var str = ArenaUtf16String.Clone(text, _arena);

        var slice = str.Slice(7, 5);
        slice.ToString().Should().Be("World");
    }

    [Fact]
    public void Slice_NegativeStart_ThrowsArgumentOutOfRangeException()
    {
        var text = "Hello, World!";
        var str = ArenaUtf16String.Clone(text, _arena);

        var act = () => str.Slice(-1, 2);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("start");
    }

    [Fact]
    public void Slice_NegativeLength_ThrowsArgumentOutOfRangeException()
    {
        var text = "Hello, World!";
        var str = ArenaUtf16String.Clone(text, _arena);

        var act = () => str.Slice(0, -1);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("length");
    }

    [Fact]
    public void Slice_Overrun_ThrowsArgumentOutOfRangeException()
    {
        var text = "Hello, World!";
        var str = ArenaUtf16String.Clone(text, _arena);

        var act = () => str.Slice(10, 10);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("length");
    }

    [Fact]
    public void Slice_IntegerOverflow_ThrowsArgumentOutOfRangeException()
    {
        var text = "Hello, World!";
        var str = ArenaUtf16String.Clone(text, _arena);

        var act = () => str.Slice(int.MaxValue - 5, 10);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("length");
    }

    [Fact]
    public void ImplicitOperator_ToReadOnlySpan_Works()
    {
        var text = "Hello";
        var str = ArenaUtf16String.Clone(text, _arena);

        ReadOnlySpan<char> span = str;
        span.SequenceEqual(text.AsSpan()).Should().BeTrue();
    }

    [Fact]
    public void Verify_AfterReset_ThrowsObjectDisposedException()
    {
        var str = ArenaUtf16String.Clone("Hello", _arena);

        _arena.Reset();

        Action act = () => str.Verify(_arena);
        act.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void IsAlive_ReflectsArenaState()
    {
        var str = ArenaUtf16String.Clone("Hello", _arena);

        str.IsAlive(_arena).Should().BeTrue();

        _arena.Reset();

        str.IsAlive(_arena).Should().BeFalse();
    }

    [Fact]
    public void Equals_IEquatable_ReturnsTrueIfEqual()
    {
        var text = "Test String";
        var str1 = ArenaUtf16String.Clone(text, _arena);
        var str2 = ArenaUtf16String.Clone(text, _arena);

        IEquatable<ArenaUtf16String> equatable = str1;
        equatable.Equals(str2).Should().BeTrue();
    }
}
