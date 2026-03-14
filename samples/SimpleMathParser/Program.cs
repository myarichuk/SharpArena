using System.Runtime.InteropServices;
using SharpArena.Allocators;
using SharpArena.Collections;
using Spectre.Console;

AnsiConsole.MarkupLine("[bold yellow]--- SharpArena Simple Math Parser ---[/]");
AnsiConsole.MarkupLine("[dim]Enter a math expression or 'exit' to quit.[/]");

using var arena = new ArenaAllocator();

while (true)
{
    var input = AnsiConsole.Ask<string>("[bold]>[/]");

    if (input.Equals("exit", StringComparison.CurrentCultureIgnoreCase))
    {
        break;
    }

    try
    {
        arena.Reset();

        var tokens = Tokenize(input, arena);
        AnsiConsole.MarkupLine("\n[bold green]Tokens:[/]");
        var table = new Table().AddColumn("Type").AddColumn("Value");
        foreach (var t in tokens.AsSpan())
        {
            table.AddRow(t.Type.ToString(), t.GetValueSpan().ToString());
        }
        AnsiConsole.Write(table);

        int result = Evaluate(tokens, arena);
        AnsiConsole.MarkupLine($"\n[bold green]Result:[/] {result}");
    }
    catch (SyntaxErrorException e)
    {
        AnsiConsole.MarkupLine($"[bold red]Syntax Error:[/] {e.Message}");
    }
    catch (Exception e)
    {
        AnsiConsole.WriteException(e);
    }

    AnsiConsole.MarkupLine("\n[dim]Arena reset for next input.[/]");
}

static ArenaList<Token> Tokenize(string input, ArenaAllocator arena)
{
    var tokens = new ArenaList<Token>(arena);
    var span = input.AsSpan();

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
            var val = ArenaString.Clone(span[start..i], arena);
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
            default: throw new SyntaxErrorException($"Unknown character: {c}");
        }
        i++;
    }

    return tokens;
}

static unsafe int Evaluate(ArenaList<Token> tokens, ArenaAllocator arena)
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

static unsafe int EvaluatePostfix(ArenaList<Token> postfix, ArenaAllocator arena)
{
    if (postfix.Length == 0)
        return 0;

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
            if (top < 2)
            {
                throw new SyntaxErrorException("Invalid expression: Not enough operands for operator.");
            }
            int b = evalStack[--top];
            int a = evalStack[--top];

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

static int Precedence(TokenType type) => type switch
{
    TokenType.Plus or TokenType.Minus => 1,
    TokenType.Multiply or TokenType.Divide => 2,
    _ => 0
};

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

[StructLayout(LayoutKind.Sequential)]
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

public class SyntaxErrorException(string message) : Exception(message);
