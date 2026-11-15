using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;
using SharpArena.Allocators;
using SharpArena.Collections;
using Varena;

namespace SharpArena.Bench;

/// <summary>
/// End-to-end lifetime cost: construct an arena (including its first segment allocation),
/// allocate N blocks, dispose. This is NOT the arena's steady-state cost; see
/// <see cref="SteadyStateBenchmarks"/> for that.
/// </summary>
[MemoryDiagnoser]
public unsafe class LifetimeBenchmarks
{
    [Params(64, 1024, 16384, 262144)]
    public int Size { get; set; }

    [Params(10, 100, 1000)]
    public int AllocationsPerRequest { get; set; }

    [Benchmark(Baseline = true, Description = "NativeMemory.Alloc/Free x N")]
    public nint NativeMemoryAllocFree()
    {
        nint last = 0;
        for (int i = 0; i < AllocationsPerRequest; i++)
        {
            void* ptr = NativeMemory.Alloc((nuint)Size);
            *(byte*)ptr = 1;
            last = (nint)ptr;
            NativeMemory.Free(ptr);
        }
        return last;
    }

    [Benchmark(Description = "Arena [P/Invoke]: create + N allocs + dispose")]
    public nint ArenaPInvokeLifetime()
    {
        using var arena = new ArenaAllocator(backend: NativeAllocatorBackend.PlatformInvoke);
        void* last = null;
        for (int i = 0; i < AllocationsPerRequest; i++)
        {
            last = arena.Alloc((nuint)Size);
            *(byte*)last = 1;
        }
        return (nint)last;
    }

    [Benchmark(Description = "Arena [NativeMemory]: create + N allocs + dispose")]
    public nint ArenaNativeMemoryLifetime()
    {
        using var arena = new ArenaAllocator(backend: NativeAllocatorBackend.DotNetUnmanaged);
        void* last = null;
        for (int i = 0; i < AllocationsPerRequest; i++)
        {
            last = arena.Alloc((nuint)Size);
            *(byte*)last = 1;
        }
        return (nint)last;
    }

    [Benchmark(Description = "Varena: create + N allocs + dispose")]
    public long VarenaLifetime()
    {
        using var manager = new VirtualArenaManager();
        var buffer = manager.CreateBuffer("BenchBuffer", 256 * 1024 * 1024);
        long sum = 0;
        for (int i = 0; i < AllocationsPerRequest; i++)
        {
            sum += buffer.AllocateRange(Size).GetHashCode();
        }
        return sum;
    }
}

/// <summary>
/// Steady-state cost, the arena's real use case: one long-lived arena, many small allocations
/// per iteration, then Reset(). The baseline does the same pattern with NativeMemory.Alloc/Free.
/// </summary>
[MemoryDiagnoser]
public unsafe class SteadyStateBenchmarks
{
    private const int Count = 1000;
    private ArenaAllocator _arena = null!;

    [Params(16, 256, 4096)]
    public int Size { get; set; }

    [GlobalSetup]
    public void Setup() => _arena = new ArenaAllocator();

    [GlobalCleanup]
    public void Cleanup() => _arena.Dispose();

    [Benchmark(Baseline = true, OperationsPerInvoke = Count, Description = "NativeMemory.Alloc + Free (per alloc)")]
    public nint NativeAllocFree()
    {
        nint last = 0;
        for (int i = 0; i < Count; i++)
        {
            void* p = NativeMemory.Alloc((nuint)Size);
            *(byte*)p = 1;
            last = (nint)p;
            NativeMemory.Free(p);
        }
        return last;
    }

    [Benchmark(OperationsPerInvoke = Count, Description = "Arena.Alloc, Reset per batch (per alloc)")]
    public nint ArenaAllocReset()
    {
        nint last = 0;
        for (int i = 0; i < Count; i++)
        {
            void* p = _arena.Alloc((nuint)Size);
            *(byte*)p = 1;
            last = (nint)p;
        }
        _arena.Reset();
        return last;
    }
}

/// <summary>
/// 1000 inserts into arena collections (including growth) followed by Reset on a long-lived
/// arena, versus the BCL equivalents.
/// </summary>
[MemoryDiagnoser]
public class CollectionBenchmarks
{
    private const int Count = 1000;
    private ArenaAllocator _arena = null!;

    // Both sides build a fresh collection per op and grow it from the default capacity:
    // reusing a cleared List/Dictionary would keep its grown capacity and skip all growth,
    // while the arena side regrows every time, which would bias the comparison.
    [GlobalSetup]
    public void Setup() => _arena = new ArenaAllocator();

    [GlobalCleanup]
    public void Cleanup() => _arena.Dispose();

    [Benchmark(Baseline = true, Description = "new List<int>, Add x1000")]
    public int ListAdd()
    {
        var list = new List<int>();
        for (int i = 0; i < Count; i++) list.Add(i);
        return list.Count;
    }

    [Benchmark(Description = "new ArenaList<int>, Add x1000 + arena.Reset")]
    public int ArenaListAdd()
    {
        var list = new ArenaList<int>(_arena);
        for (int i = 0; i < Count; i++) list.Add(i);
        int n = list.Length;
        _arena.Reset();
        return n;
    }

    [Benchmark(Description = "new Dictionary<int,int>, TryAdd x1000")]
    public int DictionaryTryAdd()
    {
        var dict = new Dictionary<int, int>();
        for (int i = 0; i < Count; i++) dict.TryAdd(i, i);
        return dict.Count;
    }

    [Benchmark(Description = "new ArenaDictionary<int,int>, TryAdd x1000 + arena.Reset")]
    public int ArenaDictionaryTryAdd()
    {
        var dict = new ArenaDictionary<int, int>(_arena);
        for (int i = 0; i < Count; i++) dict.TryAdd(i, i);
        int n = dict.Count;
        _arena.Reset();
        return n;
    }
}
