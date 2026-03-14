```

BenchmarkDotNet v0.13.12, Ubuntu 24.04.4 LTS (Noble Numbat)
Intel Xeon Processor 2.30GHz, 1 CPU, 4 logical and 4 physical cores
.NET SDK 10.0.103
  [Host]     : .NET 8.0.24 (8.0.2426.7010), X64 RyuJIT AVX2
  DefaultJob : .NET 8.0.24 (8.0.2426.7010), X64 RyuJIT AVX2


```
| Method           | Mean      | Error    | StdDev   | Ratio | Allocated | Alloc Ratio |
|----------------- |----------:|---------:|---------:|------:|----------:|------------:|
| LoopHashCode     | 364.20 ns | 4.833 ns | 4.520 ns |  1.00 |         - |          NA |
| AddBytesHashCode |  81.21 ns | 1.141 ns | 2.306 ns |  0.22 |         - |          NA |
