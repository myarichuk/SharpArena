using SharpArena.Allocators;
using SimpleMathParser;
using Xunit;
using FluentAssertions;

namespace SimpleMathParser.Tests;

public class TokenizationTests : IDisposable
{
    private readonly ArenaAllocator _arena = new(1024);

    [Fact]
    public void Tokenize_WhenInputExceedsMaxLength_ShouldThrowSyntaxErrorException()
    {
        // Arrange
        // We'll use a large string to exceed the planned 100,000 character limit.
        string input = new string('1', 100_001);

        // Act
        Action act = () => ArenaMathParser.Tokenize(input.AsSpan(), _arena);

        // Assert
        act.Should().Throw<SyntaxErrorException>()
           .WithMessage("Expression too long.");
    }

    [Fact]
    public void Tokenize_WhenInputIsWithinLimit_ShouldSucceed()
    {
        // Arrange
        string input = "1 + 2";

        // Act
        var tokens = ArenaMathParser.Tokenize(input.AsSpan(), _arena);

        // Assert
        tokens.Length.Should().Be(3);
    }

    public void Dispose()
    {
        _arena.Dispose();
    }
}
