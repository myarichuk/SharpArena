# SharpArena
Zero-alloc arena allocator + collections for high-perf parsers.

## Usage
```csharp
using SharpArena.Allocators;

using var arena = new ArenaAllocator();
// Memory allocated from arena, invalid after arena is Reset or Disposed
var ptr = arena.Alloc(1024);
arena.Reset();
```

## Benchmarks
See `bench/ArenaBench.md` for performance numbers compared to NativeMemory and Varena.
