# SimpleMathParser

A standard .NET Console application demonstrating how to build a high-performance, zero-allocation math expression parser using `SharpArena`.

It implements a simple recursive-descent / shunting-yard tokenizer and evaluator for basic math expressions.

## What it Demonstrates
- Using `ArenaAllocator` for all parser state.
- Tokenizing text with zero managed strings (using `ArenaString` concept via char pointers).
- Storing abstract syntax trees or token streams efficiently with `ArenaList<T>`.
- Using `ArenaPtrStack<T>` to maintain pointer stacks without GC pressure.
- Proving the library's lifetime safety by intentionally triggering a use-after-free check.

## Run Instructions
Make sure you have the .NET SDK installed, and simply run:

```bash
dotnet run -c Release
```

### Expected Output
```
--- SharpArena Simple Math Parser ---
Input: 2 + 3 * (10 - 4)

Tokens:
  Number: '2'
  Plus: '+'
  ...

Result: 20

Arena reset.
Lifetime safety check passed! Exception caught: Arena was reset or disposed — all pointers invalid
```
