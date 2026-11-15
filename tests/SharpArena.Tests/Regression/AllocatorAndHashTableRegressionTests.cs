using System.Reflection;
using SharpArena.Allocators;
using SharpArena.Collections;
using Xunit;

namespace SharpArena.Tests.Regression;

public unsafe class AllocatorAndHashTableRegressionTests
{
    [Fact]
    public void RoundUpToPowerOfTwo_HandlesValuesAbove2Pow32()
    {
        if (IntPtr.Size != 8) return;

        // Used to be guarded by `#if TARGET_64BIT`, which is never defined for library builds,
        // so the upper 32 bits were never smeared and the result was not a power of two.
        var method = typeof(ArenaAllocator).GetMethod("RoundUpToPowerOfTwo", BindingFlags.NonPublic | BindingFlags.Static)!;
        var result = (nuint)method.Invoke(null, [(nuint)((1UL << 33) + 1)])!;
        Assert.Equal((nuint)(1UL << 34), result);
    }

    [Fact]
    public void Alloc_NonPowerOfTwoAlignment_IsRoundedUp()
    {
        using var arena = new ArenaAllocator();
        arena.Alloc(1);
        var p = arena.Alloc(8, 24); // rounds to 32
        Assert.Equal(0u, (nuint)p % 32);
    }

    [Fact]
    public void Alloc_SizeZero_OnDisposedArena_Throws()
    {
        var arena = new ArenaAllocator();
        arena.Dispose();
        Assert.Throws<ObjectDisposedException>(() => arena.Alloc(0));
    }

    [Fact]
    public void Alloc_OversizedRequest_DoesNotStrandCurrentSegment()
    {
        using var arena = new ArenaAllocator(64 * 1024);
        var first = (byte*)arena.Alloc(1024);

        // Larger than the whole 64 KB segment, which still has ~63 KB free.
        var big = (byte*)arena.Alloc(256 * 1024);
        big[0] = 1;
        big[256 * 1024 - 1] = 2;

        // Small allocations must keep bump-allocating right after `first`, not from a new segment.
        var next = (byte*)arena.Alloc(1024);
        Assert.Equal((nuint)(first + 1024), (nuint)next);
    }

    [Fact]
    public void Alloc_OversizedRequest_DedicatedSegmentIsReusedAfterReset()
    {
        using var arena = new ArenaAllocator(64 * 1024);
        arena.Alloc(1024);
        var big1 = (byte*)arena.Alloc(256 * 1024);
        arena.Reset();

        arena.Alloc(1024);
        var big2 = (byte*)arena.Alloc(256 * 1024);

        // Same chain, same shape: the dedicated segment from the previous generation is reused.
        Assert.Equal((nuint)big1, (nuint)big2);
    }

    [Fact]
    public void Alloc_ManyMediumRequests_StillGrowsSegmentsGeometrically()
    {
        // 40 KB requests against a 64 KB segment must NOT get one dedicated mapping each.
        using var arena = new ArenaAllocator(64 * 1024);
        for (int i = 0; i < 200; i++)
        {
            var p = (byte*)arena.Alloc(40 * 1024);
            p[0] = (byte)i;
        }

        Assert.True(arena.AllocatedBytes >= 200u * 40 * 1024);
    }

    [Fact]
    public void Dictionary_StringKeys_SurviveManyGrowsAndTrim()
    {
        using var arena = new ArenaAllocator();
        var dict = new ArenaDictionary<ArenaUtf16String, int>(arena, 0);
        const int N = 5000;
        for (int i = 0; i < N; i++)
        {
            Assert.True(dict.TryAdd(ArenaUtf16String.Clone($"key-{i}", arena), i));
        }

        Assert.False(dict.TryAdd(ArenaUtf16String.Clone("key-42", arena), -1));
        dict[ArenaUtf16String.Clone("key-42", arena)] = 4242;

        dict.TrimExcess();

        for (int i = 0; i < N; i++)
        {
            Assert.True(dict.TryGetValue($"key-{i}".AsSpan(), out var v));
            Assert.Equal(i == 42 ? 4242 : i, v);
            Assert.True(dict.ContainsKey(System.Text.Encoding.UTF8.GetBytes($"key-{i}").AsSpan()));
        }

        Assert.False(dict.ContainsKey("missing".AsSpan()));
        Assert.Equal(N, dict.Count);
    }

    [Fact]
    public void Dictionary_Utf8Keys_CrossEncodingLookups()
    {
        using var arena = new ArenaAllocator();
        var dict = new ArenaDictionary<ArenaUtf8String, int>(arena);
        for (int i = 0; i < 1000; i++)
        {
            dict.Add(ArenaUtf8String.Clone($"ключ-{i}", arena), i);
        }

        for (int i = 0; i < 1000; i++)
        {
            Assert.True(dict.TryGetValue($"ключ-{i}".AsSpan(), out var v));
            Assert.Equal(i, v);
        }

        Assert.False(dict.TryGetValue("ключ-1000".AsSpan(), out _));
        Assert.True(dict.ContainsKey(ReadOnlySpan<char>.Empty) == false);
        dict.Add(default, -7);
        Assert.True(dict.TryGetValue(ReadOnlySpan<byte>.Empty, out var e));
        Assert.Equal(-7, e);
    }

    [Fact]
    public void Dictionary_UnsupportedSpanLookup_Throws()
    {
        using var arena = new ArenaAllocator();
        var dict = new ArenaDictionary<int, int>(arena);
        Assert.Throws<NotSupportedException>(() => dict.ContainsKey("x".AsSpan()));
    }

    [Fact]
    public void Set_GrowTrimAndSpanLookups()
    {
        using var arena = new ArenaAllocator();
        var set = new ArenaSet<ArenaUtf16String>(arena, 0);
        for (int i = 0; i < 3000; i++)
        {
            Assert.True(set.Add(ArenaUtf16String.Clone($"v{i}", arena)));
        }

        Assert.False(set.Add(ArenaUtf16String.Clone("v7", arena)));
        set.TrimExcess();

        for (int i = 0; i < 3000; i++)
        {
            Assert.True(set.Contains($"v{i}".AsSpan()));
            Assert.True(set.Contains(System.Text.Encoding.UTF8.GetBytes($"v{i}").AsSpan()));
        }

        Assert.False(set.Contains("v3000".AsSpan()));

        set.Clear();
        Assert.Equal(0, set.Count);
        Assert.False(set.Contains("v1".AsSpan()));
        Assert.True(set.Add(ArenaUtf16String.Clone("v1", arena)));
    }

    [Fact]
    public void Set_IntKeys_StridedPattern()
    {
        using var arena = new ArenaAllocator();
        var set = new ArenaSet<int>(arena);
        for (int i = 0; i < 20000; i++) Assert.True(set.Add(i << 16));
        for (int i = 0; i < 20000; i++) Assert.True(set.Contains(i << 16));
        Assert.False(set.Contains(1));
    }
}
