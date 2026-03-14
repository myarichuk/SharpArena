# SimpleMathParser

A modern .NET console application that demonstrates how to build a high-performance, low-allocation math expression parser using the `SharpArena` library. This example has been updated to use modern .NET 10 features and `Spectre.Console` for a rich, interactive user interface.

It implements a simple Shunting Yard algorithm for tokenizing and evaluating basic math expressions, showcasing how to handle syntax errors gracefully.

## Features
- **Modern .NET:** Utilizes .NET 10 features, including top-level statements, for a cleaner and more concise codebase.
- **Interactive UI:** Employs `Spectre.Console` to provide a visually appealing and user-friendly command-line interface.
- **High Performance:** Built with `SharpArena` to minimize memory allocations and reduce garbage collection pressure, making it highly efficient.
- **Robust Error Handling:** Implements custom exceptions for clear and informative syntax error reporting.

## What it Demonstrates
- Using `ArenaAllocator` to manage memory for all parser state.
- Tokenizing input strings with minimal overhead using `ArenaString`.
- Storing token streams efficiently with `ArenaList<T>`.
- Using `ArenaPtrStack<T>` to manage operator stacks without GC pressure.
- Gracefully handling and reporting syntax errors.

## Run Instructions
Ensure you have the .NET SDK installed. To run the application, execute the following command in your terminal:

```bash
dotnet run -c Release
```

### Example Usage
```
--- SharpArena Simple Math Parser ---
Enter a math expression or 'exit' to quit.
> 2 + 3 * (10 - 4)

Tokens:
┌──────────┬───────┐
│ Type     │ Value │
├──────────┼───────┤
│ Number   │ 2     │
│ Plus     │ +     │
│ Number   │ 3     │
│ Multiply │ *     │
│ LParen   │ (     │
│ Number   │ 10    │
│ Minus    │ -     │
│ Number   │ 4     │
│ RParen   │ )     │
└──────────┴───────┘

Result: 20

Arena reset for next input.
```
