# SharpArena benchmarks

This file intentionally contains no numbers. The previous results were produced on a different
machine (Windows) by a benchmark class that no longer exists, so they were removed. Regenerate
results on your own hardware.

## Running

```
dotnet run -c Release --project bench/SharpArena.Bench -f net10.0 -- --filter '*'
```

Use `--filter '*SteadyState*'`, `'*Lifetime*'` or `'*Collection*'` to run one group. For a quick
smoke test only (numbers from it are not meaningful): add `--job Dry`.
Results are written to `bench/SharpArena.Bench/BenchmarkDotNet.Artifacts/`.

## What is measured

All classes use `[MemoryDiagnoser]`, and every benchmark returns a value derived from what it
allocated so the work cannot be eliminated.

**LifetimeBenchmarks** - end-to-end lifetime cost: create arena (including the first segment
allocation), N allocations, dispose. Parameters: `Size` (64..262144 bytes), `AllocationsPerRequest`
(10/100/1000). This is not the arena's steady-state cost; it is dominated by construction for small N.
- `NativeMemory.Alloc/Free x N` (baseline)
- `Arena [P/Invoke]` and `Arena [NativeMemory]`: one arena per op, per backend
- `Varena`: `VirtualArenaManager` plus a 256 MiB buffer per op

**SteadyStateBenchmarks** - the arena's intended use: one arena created in `[GlobalSetup]`,
disposed in `[GlobalCleanup]`. Each invocation does 1000 allocations of `Size` (16/256/4096) bytes,
then `Reset()`. Reported per allocation (`OperationsPerInvoke`). Baseline is `NativeMemory.Alloc`
+ `Free` per allocation.

**CollectionBenchmarks** - 1000 inserts on a long-lived arena, including growth, then `Reset()`:
- fresh `ArenaList<int>` + `Add` x1000 + `Reset` vs fresh `List<int>` + `Add` x1000 (both grow from default capacity)
- fresh `ArenaDictionary<int,int>` + `TryAdd` x1000 + `Reset` vs fresh `Dictionary<int,int>` + `TryAdd` x1000
