using FluentAssertions;
using SharpArena.Allocators;
using SharpArena.Collections;
using Xunit;

namespace SharpArena.Tests.Collections;

public unsafe class ArenaUtfStringTests : IDisposable
{
    private readonly ArenaAllocator _arena = new();

    public void Dispose() => _arena.Dispose();

    [Fact]
    public void ArenaUtf8String_Clone_ValidatesContent()
    {
        var bytes = "Hello UTF-8"u8;
        var result = ArenaUtf8String.Clone(bytes, _arena);

        result.IsEmpty.Should().BeFalse();
        result.Length.Should().Be(bytes.Length);
        result.ToString().Should().Be("Hello UTF-8");
        result.AsSpan().SequenceEqual(bytes).Should().BeTrue();
    }

    [Fact]
    public void ArenaUtf16String_Clone_ValidatesContent()
    {
        var text = "Hello UTF-16";
        var result = ArenaUtf16String.Clone(text, _arena);

        result.IsEmpty.Should().BeFalse();
        result.Length.Should().Be(text.Length);
        result.ToString().Should().Be(text);
        result.AsSpan().SequenceEqual(text).Should().BeTrue();
    }

    [Fact]
    public void ArenaUtf16String_ToUtf8_EncodesCorrectly()
    {
        var text = "Hello encoding! 😊";
        var str = ArenaUtf16String.Clone(text, _arena);
        
        var utf8 = str.ToUtf8(_arena);
        
        utf8.ToString().Should().Be(text);
        utf8.Length.Should().Be(System.Text.Encoding.UTF8.GetByteCount(text));
    }

    [Fact]
    public void ArenaUtf8String_DecodeTo_DecodesCorrectly()
    {
        var text = "Hello DecodeTo!";
        var bytes = System.Text.Encoding.UTF8.GetBytes(text);
        var utf8String = ArenaUtf8String.Clone(bytes, _arena);

        var destination = new char[text.Length];
        var decodedLength = utf8String.DecodeTo(destination);

        decodedLength.Should().Be(text.Length);
        new string(destination).Should().Be(text);
    }

    [Fact]
    public void ArenaUtf8String_DecodeTo_WithEmojis()
    {
        var text = "Hello DecodeTo! 😊";
        var bytes = System.Text.Encoding.UTF8.GetBytes(text);
        var utf8String = ArenaUtf8String.Clone(bytes, _arena);

        var destination = new char[text.Length];
        var decodedLength = utf8String.DecodeTo(destination);

        decodedLength.Should().Be(text.Length);
        new string(destination).Should().Be(text);
    }

    [Fact]
    public void ArenaUtf8String_GetHashCode_IsContentBased()
    {
        var bytes = "Hash test"u8;
        var str1 = ArenaUtf8String.Clone(bytes, _arena);
        var str2 = ArenaUtf8String.Clone(bytes, _arena);

        str1.GetHashCode().Should().Be(str2.GetHashCode());
        str1.Equals(str2).Should().BeTrue();
    }

    [Fact]
    public void ArenaUtf16String_GetHashCode_IsContentBased()
    {
        var text = "Hash test";
        var str1 = ArenaUtf16String.Clone(text, _arena);
        var str2 = ArenaUtf16String.Clone(text, _arena);

        str1.GetHashCode().Should().Be(str2.GetHashCode());
        str1.Equals(str2).Should().BeTrue();
    }
}
