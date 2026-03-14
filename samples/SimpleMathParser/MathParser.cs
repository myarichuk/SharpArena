using System.Runtime.InteropServices;
using SharpArena.Allocators;
using SharpArena.Collections;

namespace SimpleMathParser;

/// <summary>
/// A reusable, zero-allocation math parser backed by an <see cref="ArenaAllocator"/>.
/// </summary>
public class ArenaMathParser
{
    /// <summary>
    /// Tokenizes the given math expression into a list of tokens allocated in the provided arena.
    /// </summary>
    /// <param name="input">The math expression as a span of characters.</param>
    /// <param name="arena">The arena allocator to use for memory allocations.</param>
    /// <returns>A list of <see cref="Token"/> structures.</returns>
    /// <exception cref="SyntaxErrorException">Thrown when an unknown character is encountered.</exception>
    public static ArenaList<Token> Tokenize(ReadOnlySpan<char> input, ArenaAllocator arena)
    {
        var tokens = new ArenaList<Token>(arena);

        int i = 0;
        while (i < input.Length)
        {
            char c = input[i];

            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            if (char.IsDigit(c))
            {
                int start = i;
                while (i < input.Length && char.IsDigit(input[i]))
                {
                    i++;
                }
                var val = ArenaString.Clone(input[start..i], arena);
                tokens.Add(new Token(TokenType.Number, val));
                continue;
            }

            var singleChar = ArenaString.Clone(input.Slice(i, 1), arena);
            switch (c)
            {
                case '+': tokens.Add(new Token(TokenType.Plus, singleChar)); break;
                case '-': tokens.Add(new Token(TokenType.Minus, singleChar)); break;
                case '*': tokens.Add(new Token(TokenType.Multiply, singleChar)); break;
                case '/': tokens.Add(new Token(TokenType.Divide, singleChar)); break;
                case '(': tokens.Add(new Token(TokenType.LParen, singleChar)); break;
                case ')': tokens.Add(new Token(TokenType.RParen, singleChar)); break;
                default: throw new SyntaxErrorException($"Unknown character: {c}");
            }
            i++;
        }

        return tokens;
    }

    /// <summary>
    /// Evaluates a list of tokens representing a math expression and returns the result.
    /// </summary>
    /// <param name="tokens">The list of tokens to evaluate.</param>
    /// <param name="arena">The arena allocator for intermediate collections.</param>
    /// <returns>The calculated result as a double.</returns>
    /// <exception cref="SyntaxErrorException">Thrown on invalid syntax or division by zero.</exception>
    public static unsafe double Evaluate(ArenaList<Token> tokens, ArenaAllocator arena)
    {
        var postfix = new ArenaList<Token>(arena, tokens.Length);
        var opStack = new ArenaPtrStack<Token>(arena);

        var span = tokens.AsSpan();
        fixed (Token* pTokens = span)
        {
            for (int i = 0; i < span.Length; i++)
            {
                Token* t = pTokens + i;

                switch (t->Type)
                {
                    case TokenType.Number:
                        postfix.Add(*t);
                        break;
                    case TokenType.LParen:
                        opStack.Push(t);
                        break;
                    case TokenType.RParen:
                    {
                        while (!opStack.IsEmpty && opStack.Peek()->Type != TokenType.LParen)
                        {
                            postfix.Add(*opStack.Pop());
                        }

                        if (opStack.IsEmpty)
                        {
                            throw new SyntaxErrorException("Mismatched parentheses: Unexpected ')'");
                        }
                        opStack.Pop(); // Pop the LParen
                        break;
                    }
                    default:
                    {
                        while (!opStack.IsEmpty && Precedence(opStack.Peek()->Type) >= Precedence(t->Type))
                        {
                            postfix.Add(*opStack.Pop());
                        }
                        opStack.Push(t);
                        break;
                    }
                }
            }

            while (!opStack.IsEmpty)
            {
                if (opStack.Peek()->Type is TokenType.LParen)
                {
                    throw new SyntaxErrorException("Mismatched parentheses: Missing ')'");
                }
                postfix.Add(*opStack.Pop());
            }
        }

        return EvaluatePostfix(postfix, arena);
    }

    private static unsafe double EvaluatePostfix(ArenaList<Token> postfix, ArenaAllocator arena)
    {
        if (postfix.Length == 0)
            return 0;

        double* evalStack = (double*)arena.Alloc((nuint)(postfix.Length * sizeof(double)), 8);
        int top = 0;

        foreach (var t in postfix.AsSpan())
        {
            if (t.Type == TokenType.Number)
            {
                evalStack[top++] = double.Parse(t.GetValueSpan());
            }
            else
            {
                if (top < 2)
                {
                    throw new SyntaxErrorException("Invalid expression: Not enough operands for operator.");
                }
                double b = evalStack[--top];
                double a = evalStack[--top];

                switch (t.Type)
                {
                    case TokenType.Plus: evalStack[top++] = a + b; break;
                    case TokenType.Minus: evalStack[top++] = a - b; break;
                    case TokenType.Multiply: evalStack[top++] = a * b; break;
                    case TokenType.Divide:
                        if (b == 0) throw new SyntaxErrorException("Division by zero.");
                        evalStack[top++] = a / b;
                        break;
                }
            }
        }

        if (top != 1)
        {
            throw new SyntaxErrorException("Invalid expression: Too many operands.");
        }

        return evalStack[0];
    }

    private static int Precedence(TokenType type) => type switch
    {
        TokenType.Plus or TokenType.Minus => 1,
        TokenType.Multiply or TokenType.Divide => 2,
        _ => 0
    };
}

/// <summary>
/// Specifies the type of a parsed math token.
/// </summary>
public enum TokenType
{
    /// <summary>A numerical value.</summary>
    Number,
    /// <summary>The addition operator (+).</summary>
    Plus,
    /// <summary>The subtraction operator (-).</summary>
    Minus,
    /// <summary>The multiplication operator (*).</summary>
    Multiply,
    /// <summary>The division operator (/).</summary>
    Divide,
    /// <summary>A left parenthesis.</summary>
    LParen,
    /// <summary>A right parenthesis.</summary>
    RParen
}

/// <summary>
/// Represents a parsed token in a mathematical expression.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct Token
{
    /// <summary>The type of the token.</summary>
    public TokenType Type;
    /// <summary>A pointer to the unmanaged character data.</summary>
    public char* ValuePtr;
    /// <summary>The length of the character data.</summary>
    public int ValueLen;

    /// <summary>
    /// Initializes a new instance of the <see cref="Token"/> struct using a raw pointer and length.
    /// </summary>
    /// <param name="type">The token type.</param>
    /// <param name="valuePtr">The pointer to the character data.</param>
    /// <param name="valueLen">The length of the character data.</param>
    public Token(TokenType type, char* valuePtr, int valueLen)
    {
        Type = type;
        ValuePtr = valuePtr;
        ValueLen = valueLen;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Token"/> struct using an <see cref="ArenaString"/>.
    /// </summary>
    /// <param name="type">The token type.</param>
    /// <param name="str">The arena string containing the token text.</param>
    public Token(TokenType type, ArenaString str)
    {
        Type = type;
        fixed (char* ptr = str.AsSpan())
        {
            ValuePtr = ptr;
        }
        ValueLen = str.Length;
    }

    /// <summary>
    /// Gets the character span representing the token's value.
    /// </summary>
    /// <returns>A read-only span of characters.</returns>
    public ReadOnlySpan<char> GetValueSpan()
    {
        return new ReadOnlySpan<char>(ValuePtr, ValueLen);
    }
}

/// <summary>
/// Exception thrown when a mathematical expression contains syntax errors.
/// </summary>
public class SyntaxErrorException(string message) : Exception(message);
