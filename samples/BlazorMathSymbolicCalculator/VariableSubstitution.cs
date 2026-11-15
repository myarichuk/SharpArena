using System.Text.RegularExpressions;

namespace BlazorMathSymbolicCalculator;

/// <summary>
/// Substitutes user-defined variables (e.g. "x=1+2, y=3") into an expression.
/// </summary>
public static class VariableSubstitution
{
    private static readonly Regex Identifier = new(@"\b[A-Za-z_][A-Za-z0-9_]*\b", RegexOptions.Compiled);

    /// <summary>
    /// Parses "k=v, k2=v2" text into a dictionary. Empty/whitespace keys are skipped; later entries win.
    /// </summary>
    public static Dictionary<string, string> ParseVariables(string? variablesText)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(variablesText)) return map;

        foreach (var pair in variablesText.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var kvp = pair.Split('=', 2);
            if (kvp.Length != 2) continue;
            var key = kvp[0].Trim();
            var val = kvp[1].Trim();
            if (key.Length == 0 || val.Length == 0) continue;
            map[key] = val;
        }
        return map;
    }

    /// <summary>
    /// Replaces whole identifiers in <paramref name="expression"/> in a single pass, wrapping each
    /// substituted value in parentheses so "x=1+2" makes "2 * x" become "2 * (1+2)".
    /// </summary>
    public static string Apply(string expression, string? variablesText)
        => Apply(expression, ParseVariables(variablesText));

    /// <inheritdoc cref="Apply(string, string?)"/>
    public static string Apply(string expression, IReadOnlyDictionary<string, string> variables)
    {
        if (variables.Count == 0) return expression;
        return Identifier.Replace(expression, m =>
            variables.TryGetValue(m.Value, out var val) ? "(" + val + ")" : m.Value);
    }
}
