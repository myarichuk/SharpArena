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

        var tokens = SimpleMathParser.ArenaMathParser.Tokenize(input, arena);
        AnsiConsole.MarkupLine("\n[bold green]Tokens:[/]");
        var table = new Table().AddColumn("Type").AddColumn("Value");
        foreach (var t in tokens.AsSpan())
        {
            table.AddRow(t.Type.ToString(), t.GetValueSpan().ToString());
        }
        AnsiConsole.Write(table);

        double result = SimpleMathParser.ArenaMathParser.Evaluate(tokens, arena);
        AnsiConsole.MarkupLine($"\n[bold green]Result:[/] {result}");
    }
    catch (SimpleMathParser.SyntaxErrorException e)
    {
        AnsiConsole.MarkupLine($"[bold red]Syntax Error:[/] {e.Message}");
    }
    catch (Exception e)
    {
        AnsiConsole.WriteException(e);
    }

    AnsiConsole.MarkupLine("\n[dim]Arena reset for next input.[/]");
}
