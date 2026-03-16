using SharpArena.Allocators;
using SharpArena.Collections;
using Xunit;

namespace SharpArena.Tests.Collections;

public class ArenaListStressTests
{
    [Fact]
    public void ArenaList_Stress_RapidGrowth_1Million_IntegrityAndNoOverflow()
    {
        using var arena = new ArenaAllocator(16 * 1024 * 1024); // give it breathing room
        var list = new ArenaList<long>(arena, 16);

        const int N = 1_000_000;
        for (int i = 0; i < N; i++)
        {
            list.Add(i);
        }

        Assert.Equal(N, list.Length);
        Assert.True(list.Capacity >= N); // doubled enough times
        Assert.True(list.Capacity <= N * 2);// no ridiculous over-allocation

        // data must survive all those memcpy calls
        Assert.Equal(0L, list[0]);
        Assert.Equal(N - 1L, list[^1]);

        // spot-check every 12345th element (so test is fast enough)
        for (int i = 0; i < N; i += 12_345)
        {
            Assert.Equal((long)i, list[i]);
        }
    }

    [Fact]
    public void ArenaList_Stress_RepeatedResetReuse_1000Cycles_NoLeakNoCrash()
    {
        using var arena = new ArenaAllocator();
        var list = new ArenaList<int>(arena, 4);

        for (int cycle = 0; cycle < 1_000; cycle++)
        {
            for (int i = 0; i < 10_000; i++)
            {
                list.Add(i);
            }

            Assert.Equal(10_000, list.Length);

            list.Reset(); // only Count=0, buffers stay
            Assert.Equal(0, list.Length);
        }

        // after all that, arena.Reset() should still kill the handle
        arena.Reset();
        Assert.Throws<ObjectDisposedException>(() => list.Add(1));
    }

    [Fact]
    public void ArenaList_Stress_ManySmallLists_OneArena_HeaderPressure()
    {
        using var arena = new ArenaAllocator(64 * 1024 * 1024);
        const int ListsCount = 5_000;
        var lists = new ArenaList<int>[ListsCount];

        for (int i = 0; i < ListsCount; i++)
        {
            lists[i] = new ArenaList<int>(arena, 8);
            for (int j = 0; j < 64; j++)
            {
                lists[i].Add(i * 100 + j);
            }
        }

        // verify independence
        for (int i = 0; i < ListsCount; i++)
        {
            Assert.Equal(64, lists[i].Length);
            Assert.Equal(i * 100, lists[i][0]);
        }

        arena.Reset();
        foreach (var l in lists)
            Assert.Throws<ObjectDisposedException>(() => l[0] = 99);
    }

    [Fact]
    public void ArenaList_Stress_StructCopy_MutationsVisibleAcrossCopies_UnderLoad()
    {
        using var arena = new ArenaAllocator();
        var original = new ArenaList<int>(arena, 16);

        // fill it up so growth happen
        for (int i = 0; i < 100_000; i++)
        {
            original.Add(i);
        }

        var copy1 = original;
        var copy2 = original;

        copy1.Add(999_999);
        copy2[0] = -1;

        // all three handles see the same header
        Assert.Equal(100_001, original.Length);
        Assert.Equal(100_001, copy1.Length);
        Assert.Equal(100_001, copy2.Length);
        Assert.Equal(-1, original[0]);
        Assert.Equal(999_999, original[^1]);
    }
}