using System;
using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using SharpArena.Collections;
using SharpArena.Allocators;

[MemoryDiagnoser]
public class HashCodeBench
{
    private ArenaAllocator _arena;
    private ArenaString _arenaString;
    private ReadOnlyMemory<char> _memory;

    [GlobalSetup]
    public void Setup()
    {
        _arena = new ArenaAllocator(1024 * 1024);
        string source = new string('a', 100);
        _arenaString = ArenaString.Clone(source.AsSpan(), _arena);
    }

    [Benchmark(Baseline = true)]
    public int LoopHashCode()
    {
        var hash = new HashCode();
        hash.Add(_arenaString.Length);

        foreach (var ch in _arenaString.AsSpan())
        {
            hash.Add(ch);
        }

        return hash.ToHashCode();
    }

    [Benchmark]
    public int AddBytesHashCode()
    {
        var hash = new HashCode();
        hash.Add(_arenaString.Length);
        hash.AddBytes(MemoryMarshal.AsBytes(_arenaString.AsSpan()));
        return hash.ToHashCode();
    }
}

class Program
{
    static void Main(string[] args)
    {
        var summary = BenchmarkRunner.Run<HashCodeBench>();
    }
}
