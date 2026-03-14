using System;
using SharpArena.Allocators;
using SharpArena.Collections;

namespace SimpleMathParser;

public enum TokenType
{
    Number,
    Plus,
    Minus,
    Multiply,
    Divide,
    LParen,
    RParen
}

public unsafe struct Token
{
    public TokenType Type;
    public char* ValuePtr;
    public int ValueLen;

    public Token(TokenType type, char* valuePtr, int valueLen)
    {
        Type = type;
        ValuePtr = valuePtr;
        ValueLen = valueLen;
    }

    public Token(TokenType type, ArenaString str)
    {
        Type = type;
        fixed (char* ptr = str.AsSpan())
        {
            ValuePtr = ptr;
        }
        ValueLen = str.Length;
    }

    public ReadOnlySpan<char> GetValueSpan()
    {
        return new ReadOnlySpan<char>(ValuePtr, ValueLen);
    }
}

public static class Program
{
    public static void Main()
    {
        Console.WriteLine("--- SharpArena Simple Math Parser ---");

        using var arena = new ArenaAllocator();
        string input = "2 + 3 * (10 - 4)";
        Console.WriteLine($"Input: {input}");

        // 1. Tokenize
        var tokens = Tokenize(input, arena);

        Console.WriteLine("\nTokens:");
        foreach (var t in tokens.AsSpan())
        {
            Console.WriteLine($"  {t.Type}: '{t.GetValueSpan().ToString()}'");
        }

        // 2. Parse & Evaluate (Shunting Yard + Eval)
        int result = Evaluate(tokens, arena);
        Console.WriteLine($"\nResult: {result}");

        // 3. Reset demo
        arena.Reset();
        Console.WriteLine("\nArena reset.");

        try
        {
            // Lifetime safety check: attempting to access arena-bound collection
            // after reset should throw ObjectDisposedException
            _ = tokens.Length;
            Console.WriteLine("Uh oh, this should have thrown!");
        }
        catch (ObjectDisposedException ex)
        {
            Console.WriteLine($"Lifetime safety check passed! Exception caught: {ex.Message}");
        }
    }

    private static unsafe ArenaList<Token> Tokenize(string input, ArenaAllocator arena)
    {
        var tokens = new ArenaList<Token>(arena, 16);
        ReadOnlySpan<char> span = input.AsSpan();

        int i = 0;
        while (i < span.Length)
        {
            char c = span[i];

            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            if (char.IsDigit(c))
            {
                int start = i;
                while (i < span.Length && char.IsDigit(span[i]))
                {
                    i++;
                }
                var val = ArenaString.Clone(span.Slice(start, i - start), arena);
                tokens.Add(new Token(TokenType.Number, val));
                continue;
            }

            var singleChar = ArenaString.Clone(span.Slice(i, 1), arena);
            switch (c)
            {
                case '+': tokens.Add(new Token(TokenType.Plus, singleChar)); break;
                case '-': tokens.Add(new Token(TokenType.Minus, singleChar)); break;
                case '*': tokens.Add(new Token(TokenType.Multiply, singleChar)); break;
                case '/': tokens.Add(new Token(TokenType.Divide, singleChar)); break;
                case '(': tokens.Add(new Token(TokenType.LParen, singleChar)); break;
                case ')': tokens.Add(new Token(TokenType.RParen, singleChar)); break;
                default: throw new Exception($"Unknown char: {c}");
            }
            i++;
        }

        return tokens;
    }

    private static unsafe int Evaluate(ArenaList<Token> tokens, ArenaAllocator arena)
    {
        var postfix = new ArenaList<Token>(arena, tokens.Length);
        var opStack = new ArenaPtrStack<Token>(arena, 16);

        var span = tokens.AsSpan();
        fixed (Token* pTokens = span)
        {
            for (int i = 0; i < span.Length; i++)
            {
                Token* t = pTokens + i;

                if (t->Type == TokenType.Number)
                {
                    postfix.Add(*t);
                }
                else if (t->Type == TokenType.LParen)
                {
                    opStack.Push(t);
                }
                else if (t->Type == TokenType.RParen)
                {
                    while (!opStack.IsEmpty && opStack.Peek()->Type != TokenType.LParen)
                    {
                        postfix.Add(*opStack.Pop());
                    }
                    if (!opStack.IsEmpty && opStack.Peek()->Type == TokenType.LParen)
                    {
                        opStack.Pop();
                    }
                }
                else
                {
                    while (!opStack.IsEmpty && Precedence(opStack.Peek()->Type) >= Precedence(t->Type))
                    {
                        postfix.Add(*opStack.Pop());
                    }
                    opStack.Push(t);
                }
            }

            while (!opStack.IsEmpty)
            {
                postfix.Add(*opStack.Pop());
            }
        }

        return EvaluatePostfix(postfix, arena);
    }

    private static unsafe int EvaluatePostfix(ArenaList<Token> postfix, ArenaAllocator arena)
    {
        int* evalStack = (int*)arena.Alloc((nuint)(postfix.Length * sizeof(int)), 4);
        int top = 0;

        foreach (var t in postfix.AsSpan())
        {
            if (t.Type == TokenType.Number)
            {
                evalStack[top++] = int.Parse(t.GetValueSpan());
            }
            else
            {
                int b = evalStack[--top];
                int a = evalStack[--top];

                switch (t.Type)
                {
                    case TokenType.Plus: evalStack[top++] = a + b; break;
                    case TokenType.Minus: evalStack[top++] = a - b; break;
                    case TokenType.Multiply: evalStack[top++] = a * b; break;
                    case TokenType.Divide: evalStack[top++] = a / b; break;
                }
            }
        }

        return evalStack[0];
    }

    private static int Precedence(TokenType type)
    {
        return type switch
        {
            TokenType.Plus or TokenType.Minus => 1,
            TokenType.Multiply or TokenType.Divide => 2,
            _ => 0
        };
    }
}
