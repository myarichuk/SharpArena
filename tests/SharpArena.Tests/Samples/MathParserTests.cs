using System;
using SharpArena.Allocators;
using SharpArena.Collections;
using SimpleMathParser;
using Xunit;
using FluentAssertions;

namespace SharpArena.Tests.Samples;

public class MathParserTests
{
    [Fact]
    public void Evaluate_InvalidNumber_ThrowsSyntaxErrorException()
    {
        // Arrange
        using var arena = new ArenaAllocator();
        var tokens = new ArenaList<Token>(arena);

        // Manually create a token with an invalid number to bypass Tokenize's digit-only logic
        var invalidNum = ArenaString.Clone("not-a-number", arena);
        tokens.Add(new Token(TokenType.Number, invalidNum));

        // Act
        Action act = () => ArenaMathParser.Evaluate(tokens, arena);

        // Assert
        // Before fix: This throws FormatException (unhandled)
        // After fix: Should throw SyntaxErrorException
        act.Should().Throw<SyntaxErrorException>().WithMessage("Invalid number format: not-a-number");
    }

    [Fact]
    public void Evaluate_ValidExpression_ReturnsResult()
    {
        // Arrange
        using var arena = new ArenaAllocator();
        var tokens = ArenaMathParser.Tokenize("1+2*3", arena);

        // Act
        double result = ArenaMathParser.Evaluate(tokens, arena);

        // Assert
        result.Should().Be(7.0);
    }
}
