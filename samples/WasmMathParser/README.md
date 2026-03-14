# WasmMathParser

A WASI console application demonstrating that `SharpArena` works identically in a WASM environment (unlike virtual-memory based allocators like `Varena`).

It runs the exact same shunting-yard logic and demonstrates zero managed allocations outside the arena, even under constraints where memory pages are managed differently.

## Run Instructions

Make sure you have the required WASM tools and `.NET 10.0` SDK.
`wasi-wasm` target usually requires `wasmtime` or another WASI runtime on your system to run via `dotnet run`.

1. Publish or Build the project for `wasi-wasm`:

```bash
dotnet build -c Release
```

2. Run with your local `.NET` / `wasmtime` environment:
```bash
dotnet run -c Release
```

### Expected Output
```
--- SharpArena WASM Math Parser ---
Input: 2 + 3 * (10 - 4)

Tokens:
  Number: '2'
  Plus: '+'
  ...

Result: 20

Arena reset.
Lifetime safety check passed! Exception caught: Arena was reset or disposed — all pointers invalid
```
