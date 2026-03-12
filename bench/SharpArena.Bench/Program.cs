using System;
using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;
using SharpArena.Allocators;
using Varena;

namespace SharpArena.Bench;

[MemoryDiagnoser]
[MarkdownExporterAttribute.GitHub]
public class ArenaBenchmark
{
    [Params(64, 1024, 16384, 262144)]
    public int Size { get; set; }

    [Params(10, 100, 1000)]
    public int AllocationsPerRequest { get; set; }

    [Benchmark(Baseline = true, Description = "NativeMemory.Alloc/Free")]
    public unsafe void NativeMemoryAllocFree()
    {
        for (int i = 0; i < AllocationsPerRequest; i++)
        {
            void* ptr = NativeMemory.Alloc((nuint)Size);
            NativeMemory.Free(ptr);
        }
    }

    [Benchmark(Description = "ArenaAllocator [P/Invoke] (create -> alloc -> dispose)")]
    public unsafe void SharpArenaPInvoke()
    {
        using var arena = new ArenaAllocator(backend: NativeAllocatorBackend.PlatformInvoke);
        for (int i = 0; i < AllocationsPerRequest; i++)
        {
            arena.Alloc((nuint)Size);
        }
    }

    [Benchmark(Description = "ArenaAllocator [NativeMemory.Alloc] (create -> alloc -> dispose)")]
    public unsafe void SharpArenaNativeMemory()
    {
        using var arena = new ArenaAllocator(backend: NativeAllocatorBackend.DotNetUnmanaged);
        for (int i = 0; i < AllocationsPerRequest; i++)
        {
            arena.Alloc((nuint)Size);
        }
    }

    [Benchmark(Description = "Varena (create -> alloc -> dispose)")]
    public unsafe void VarenaAlloc()
    {
        using var manager = new VirtualArenaManager();
        var buffer = manager.CreateBuffer("BenchBuffer", 256 * 1024 * 1024);
        for (int i = 0; i < AllocationsPerRequest; i++)
        {
            buffer.AllocateRange(Size);
        }
    }
}

public class Program
{
    public static void Main(string[] args)
    {
        // Define paths relative to the project directory to ensure it works regardless of cwd
        var projectDir = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
        // Navigate up from bin/Release/net10.0/ to the project root
        while (projectDir != null && !System.IO.Directory.Exists(System.IO.Path.Combine(projectDir, "obj")))
        {
            projectDir = System.IO.Directory.GetParent(projectDir)?.FullName;
        }

        if (projectDir == null)
        {
            projectDir = System.IO.Directory.GetCurrentDirectory();
        }

        var artifactsPath = System.IO.Path.Combine(projectDir, "BenchmarkDotNet.Artifacts");

        var config = DefaultConfig.Instance
            .WithArtifactsPath(artifactsPath);

        var summary = BenchmarkRunner.Run<ArenaBenchmark>(config);

        // Copy the generated markdown to bench/ArenaBench.md
        // We use the -github.md file because of [MarkdownExporterAttribute.GitHub]
        var sourceFile = System.IO.Path.Combine(artifactsPath, "results", "SharpArena.Bench.ArenaBenchmark-report-github.md");

        // The destination file is bench/ArenaBench.md which is one level up from bench/SharpArena.Bench
        var destFile = System.IO.Path.Combine(System.IO.Directory.GetParent(projectDir)!.FullName, "ArenaBench.md");

        if (System.IO.File.Exists(sourceFile))
        {
            System.IO.File.Copy(sourceFile, destFile, overwrite: true);
            Console.WriteLine($"Successfully updated {destFile}");
        }
        else
        {
            Console.WriteLine($"Failed to find source file {sourceFile}");
        }
    }
}
