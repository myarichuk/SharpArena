using SharpArena.Allocators;
using FluentAssertions;
using Xunit;

namespace SimpleMathParser.Tests;

public class EvaluationTests : IDisposable
{
    private readonly ArenaAllocator _arena = new(1024);

    private double Eval(string s)
    {
        var tokens = ArenaMathParser.Tokenize(s.AsSpan(), _arena);
        return ArenaMathParser.Evaluate(tokens, _arena);
    }

    [Theory]
    [InlineData("1+2*3", 7)]
    [InlineData("(1+2)*3", 9)]
    [InlineData("10-4-3", 3)]
    [InlineData("8/2/2", 2)]
    [InlineData("42", 42)]
    [InlineData("-3", -3)]
    [InlineData("+3", 3)]
    [InlineData("2*-3", -6)]
    [InlineData("2--3", 5)]
    [InlineData("-(1+2)", -3)]
    [InlineData("-2*3", -6)]
    [InlineData("--2", 2)]
    [InlineData("(-1)*(-1)", 1)]
    [InlineData("1.5+1", 2.5)]
    [InlineData(".5*4", 2)]
    [InlineData("0.1+0.2", 0.30000000000000004)]
    public void Evaluate_ValidExpressions(string expr, double expected)
    {
        Eval(expr).Should().Be(expected);
    }

    [Fact]
    public void Evaluate_IsCultureInvariant()
    {
        var old = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            Eval("1.5+1").Should().Be(2.5);
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = old; }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("()")]
    [InlineData("2 3 +")]
    [InlineData("2 3")]
    [InlineData("+ 2 3")]
    [InlineData("* 2")]
    [InlineData("2 (3)")]
    [InlineData("(1)(2)")]
    [InlineData("1 +")]
    [InlineData("1 + * 2")]
    [InlineData("(1+)")]
    [InlineData("(1")]
    [InlineData("1)")]
    [InlineData(")(")]
    [InlineData("1.2.3")]
    [InlineData("1 / 0")]
    [InlineData("2 $ 3")]
    public void Evaluate_InvalidExpressions_Throw(string expr)
    {
        Action act = () => Eval(expr);
        act.Should().Throw<SyntaxErrorException>();
    }

    [Theory]
    [InlineData("x=1+2", "2 * x", "2 * (1+2)")]
    [InlineData("x=5", "max + x", "max + (5)")]
    [InlineData("x=1, xy=2", "xy + x", "(2) + (1)")]
    [InlineData("=5, x=2", "x", "(2)")]
    [InlineData(" =5", "x", "x")]
    [InlineData("", "x+1", "x+1")]
    public void VariableSubstitution_Apply(string vars, string expr, string expected)
    {
        BlazorMathSymbolicCalculator.VariableSubstitution.Apply(expr, vars).Should().Be(expected);
    }

    [Fact]
    public void VariableSubstitution_Result_Evaluates_Correctly()
    {
        var s = BlazorMathSymbolicCalculator.VariableSubstitution.Apply("2 * x", "x=1+2");
        Eval(s).Should().Be(6);
    }

    public void Dispose() => _arena.Dispose();
}
