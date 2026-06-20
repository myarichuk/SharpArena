```

BenchmarkDotNet v0.15.8, Linux Ubuntu 24.04.4 LTS (Noble Numbat)
Intel Xeon Processor 2.30GHz, 1 CPU, 4 logical and 4 physical cores
.NET SDK 10.0.103
  [Host]     : .NET 8.0.24 (8.0.24, 8.0.2426.7010), X64 RyuJIT x86-64-v3
  DefaultJob : .NET 8.0.24 (8.0.24, 8.0.2426.7010), X64 RyuJIT x86-64-v3


```
| Method          | Mean       | Error    | StdDev   | Ratio | Allocated | Alloc Ratio |
|---------------- |-----------:|---------:|---------:|------:|----------:|------------:|
| Equals_Current  | 9,571.7 ns | 14.67 ns | 13.00 ns |  1.00 |         - |          NA |
| Equals_Proposed |   715.1 ns | 14.21 ns | 13.96 ns |  0.07 |         - |          NA |
