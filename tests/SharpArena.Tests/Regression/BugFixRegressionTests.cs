using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using FluentAssertions;
using SharpArena.Allocators;
using SharpArena.Collections;
using Xunit;

namespace SharpArena.Tests.Regression;

/// <summary>
/// Regression tests for the code-review findings tracked as C1-C6 (critical), H1-H6 (high),
/// and M1-M7 (medium). Each test is named after the finding it guards against.
/// </summary>
public unsafe class BugFixRegressionTests
{
    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct Big4KStruct
    {
        public fixed byte Data[4096];
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Size24Struct
    {
        public long A;
        public long B;
        public long C;
    }

    // ---- C1: lookup misses on tiny hash tables must terminate, not hang ----

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void ArenaDictionary_TinyCapacity_LookupMiss_DoesNotHang(int requestedCapacity)
    {
        using var arena = new ArenaAllocator();
        var dict = new ArenaDictionary<int, int>(arena, requestedCapacity);
        dict.TryAdd(1, 100);

        var completed = Task.Run(() => dict.ContainsKey(999)).Wait(TimeSpan.FromSeconds(5));
        completed.Should().BeTrue("a lookup miss must always find an empty bucket to stop at");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void ArenaSet_TinyCapacity_LookupMiss_DoesNotHang(int requestedCapacity)
    {
        using var arena = new ArenaAllocator();
        var set = new ArenaSet<int>(arena, requestedCapacity);
        set.Add(1);

        var completed = Task.Run(() => set.Contains(999)).Wait(TimeSpan.FromSeconds(5));
        completed.Should().BeTrue("a lookup miss must always find an empty bucket to stop at");
    }

    [Fact]
    public void ArenaDictionary_TrimExcessOnEmpty_DoesNotHang()
    {
        using var arena = new ArenaAllocator();
        var dict = new ArenaDictionary<int, int>(arena, 16);
        dict.TrimExcess();
        dict.TryAdd(1, 1);

        var completed = Task.Run(() => dict.ContainsKey(2)).Wait(TimeSpan.FromSeconds(5));
        completed.Should().BeTrue();
        dict.TryGetValue(1, out var value).Should().BeTrue();
        value.Should().Be(1);
    }

    [Fact]
    public void ArenaSet_TrimExcessOnEmpty_DoesNotHang()
    {
        using var arena = new ArenaAllocator();
        var set = new ArenaSet<int>(arena, 16);
        set.TrimExcess();
        set.Add(1);

        var completed = Task.Run(() => set.Contains(2)).Wait(TimeSpan.FromSeconds(5));
        completed.Should().BeTrue();
        set.Contains(1).Should().BeTrue();
    }

    [Fact]
    public void ArenaDictionary_InitialCapacityAboveMax_Throws()
    {
        using var arena = new ArenaAllocator();
        Action act = () => new ArenaDictionary<int, int>(arena, (1 << 30) + 1);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ArenaSet_InitialCapacityAboveMax_Throws()
    {
        using var arena = new ArenaAllocator();
        Action act = () => new ArenaSet<int>(arena, (1 << 30) + 1);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // ---- C2: Dispose must invalidate generations; generations must not collide across arenas ----

    [Fact]
    public void ArenaAllocator_Dispose_InvalidatesGenerationImmediately()
    {
        var arena = new ArenaAllocator();
        var dict = new ArenaDictionary<int, int>(arena, 16);
        dict.TryAdd(1, 1);

        arena.Dispose();

        Action act = () => dict.ContainsKey(1);
        act.Should().Throw<ObjectDisposedException>("freed memory must never be dereferenced after Dispose");
    }

    [Fact]
    public void ArenaUtf16String_IsAlive_ReturnsFalseForADifferentArena()
    {
        using var arenaA = new ArenaAllocator();
        using var arenaB = new ArenaAllocator();

        var s = ArenaUtf16String.Clone("hello", arenaA);

        s.IsAlive(arenaA).Should().BeTrue();
        s.IsAlive(arenaB).Should().BeFalse("two different arenas must never share a generation number");
    }

    // ---- C3: large allocations combined with alignment padding must not spuriously OOM ----

    [Fact]
    public void ArenaList_OfLargeAlignedStruct_DoesNotThrowSpuriousOutOfMemory()
    {
        using var arena = new ArenaAllocator();
        Action act = () => new ArenaList<Big4KStruct>(arena, 256);
        act.Should().NotThrow();
    }

    [Fact]
    public void ArenaAllocator_AllocWithSizeNearSegmentBoundaryAndAlignment_Succeeds()
    {
        using var arena = new ArenaAllocator();
        Action act = () => arena.Alloc(1024 * 1024, 64);
        act.Should().NotThrow();
    }

    // ---- C4: alignment must always be rounded to a power of two ----

    [Fact]
    public void ArenaBlockList_Of24ByteStruct_DoesNotMisalignOrAssert()
    {
        using var arena = new ArenaAllocator();
        var list = new ArenaBlockList<Size24Struct>(arena, 4);

        for (int i = 0; i < 20; i++)
        {
            list.Add(new Size24Struct { A = i, B = i * 2, C = i * 3 });
        }

        var span = list.GetSpan();
        span.Length.Should().Be(20);
        for (int i = 0; i < 20; i++)
        {
            span[i].A.Should().Be(i);
            span[i].B.Should().Be(i * 2);
            span[i].C.Should().Be(i * 3);
        }
    }

    [Fact]
    public void ArenaAllocator_AllocWithNonPowerOfTwoAlignment_RoundsUpInsteadOfMisaligning()
    {
        using var arena = new ArenaAllocator();
        var ptr = arena.Alloc(64, align: 24);

        // 24 is not a power of two; the returned pointer must be aligned to the next one (32),
        // not merely to a multiple of 24.
        ((nuint)ptr % 32).Should().Be(0);
    }

    // ---- C5: a zero block size must be rejected, not silently corrupt memory ----

    [Fact]
    public void ArenaBlockList_ZeroBlockSize_Throws()
    {
        using var arena = new ArenaAllocator();
        Action act = () => new ArenaBlockList<int>(arena, 0);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // ---- C6: Free on a protected allocation must not crash the process ----

    [Theory]
    [InlineData(MemoryProtectionMode.ReadOnly)]
    [InlineData(MemoryProtectionMode.NoAccess)]
    public void NativeAllocator_FreeOfProtectedAllocation_DoesNotCrash(MemoryProtectionMode mode)
    {
        var ptr = NativeAllocator.Alloc(4096, NativeAllocatorBackend.PlatformInvoke, mode);

        Action act = () => NativeAllocator.Free(ptr, NativeAllocatorBackend.PlatformInvoke);
        act.Should().NotThrow();
    }

    // ---- H1: duplicate elements in `other` must not be double-counted ----

    [Fact]
    public void ArenaSet_SetEquals_DoesNotDoubleCountDuplicatesInOther()
    {
        using var arena = new ArenaAllocator();
        var set = new ArenaSet<int>(arena);
        set.Add(1);

        set.SetEquals(new[] { 1, 1 }).Should().BeTrue();
    }

    [Fact]
    public void ArenaSet_IsProperSupersetOf_DoesNotDoubleCountDuplicatesInOther()
    {
        using var arena = new ArenaAllocator();
        var set = new ArenaSet<int>(arena);
        set.Add(1);
        set.Add(2);

        set.IsProperSupersetOf(new[] { 1, 1 }).Should().BeTrue();
    }

    // ---- H2: Add() after Reset() must reuse the existing block chain ----

    [Fact]
    public void ArenaBlockList_ResetThenRefill_ReusesChain_CapacityStaysStable()
    {
        using var arena = new ArenaAllocator();
        var list = new ArenaBlockList<int>(arena, blockSize: 4);

        for (int i = 0; i < 14; i++) list.Add(i);

        var capacityAfterFirstFill = list.Capacity;
        var bytesAfterFirstFill = arena.AllocatedBytes;

        list.Reset();
        for (int i = 0; i < 14; i++) list.Add(i);

        list.Capacity.Should().Be(capacityAfterFirstFill, "Reset should let Add reuse already-linked blocks");
        arena.AllocatedBytes.Should().Be(bytesAfterFirstFill, "refilling after Reset must not allocate new blocks");
    }

    // ---- H4: null/"" must compare consistently; Equals(object) must not accept string ----

    [Fact]
    public void ArenaUtf8String_DefaultEqualsEmptyString_MatchesEqualsNull()
    {
        default(ArenaUtf8String).Equals("").Should().BeTrue();
        default(ArenaUtf8String).Equals((string?)null).Should().BeTrue();
    }

    [Fact]
    public void ArenaUtf8String_EqualsObject_DoesNotAcceptManagedString()
    {
        using var arena = new ArenaAllocator();
        var s = ArenaUtf8String.Clone("hello", arena);

        s.Equals((object)"hello").Should().BeFalse("Equals(object) accepting string would violate the GetHashCode contract");
        s.Equals("hello").Should().BeTrue("the typed Equals(string) overload is unaffected");
    }

    // ---- M1: sequential/strided keys must not degrade probing to quadratic time ----

    [Fact]
    public void ArenaDictionary_StridedKeys_DoNotDegradeToQuadraticProbing()
    {
        using var arena = new ArenaAllocator(4 * 1024 * 1024, 512 * 1024 * 1024);
        var dict = new ArenaDictionary<int, int>(arena, 1 << 17);

        const int keyCount = 40_000;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < keyCount; i++)
        {
            dict.TryAdd(i << 16, i).Should().BeTrue();
        }
        for (int i = 0; i < keyCount; i++)
        {
            dict.TryGetValue(i << 16, out var value).Should().BeTrue();
            value.Should().Be(i);
        }
        sw.Stop();

        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(3),
            "unmixed hashing on strided keys previously took ~900ms for this workload; a regression would be far worse");
    }

    // ---- M2/M5: TryResizeInPlace should avoid wasting arena memory on shrink ----

    [Fact]
    public void ArenaList_TrimExcess_ShrinksInPlace()
    {
        using var arena = new ArenaAllocator();
        var list = new ArenaList<byte>(arena, 1024);
        for (int i = 0; i < 10; i++) list.Add((byte)i);

        var beforeTrim = arena.AllocatedBytes;
        list.TrimExcess();
        var afterTrim = arena.AllocatedBytes;

        afterTrim.Should().BeLessThan(beforeTrim);
        list.Capacity.Should().Be(10);
    }

    [Fact]
    public void ArenaUtf16String_ToUtf8_DoesNotPermanentlyWasteWorstCaseSpace()
    {
        using var arena = new ArenaAllocator();
        var s = ArenaUtf16String.Clone(new string('a', 100), arena); // pure ASCII: 1 byte per char in UTF-8

        var beforeBytes = arena.AllocatedBytes;
        var utf8 = s.ToUtf8(arena);
        var afterBytes = arena.AllocatedBytes;

        utf8.Length.Should().Be(100);
        (afterBytes - beforeBytes).Should().Be(100, "the 3x GetMaxByteCount reservation should be shrunk back after encoding");
    }

    // ---- M3: maxSize must not be smaller than initialSize ----

    [Fact]
    public void ArenaAllocator_MaxSizeLessThanInitialSize_Throws()
    {
        Action act = () => new ArenaAllocator(initialSize: 8192, maxSize: 4096);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // ---- M4: foreach must bind to the struct enumerator directly, not the boxed interface ----

    [Fact]
    public void ArenaDictionary_GetEnumerator_ReturnsConcreteStructType()
    {
        using var arena = new ArenaAllocator();
        var dict = new ArenaDictionary<int, int>(arena);
        var enumerator = dict.GetEnumerator();

        enumerator.Should().BeOfType<ArenaDictionary<int, int>.Enumerator>();
    }

    [Fact]
    public void ArenaSet_GetEnumerator_ReturnsConcreteStructType()
    {
        using var arena = new ArenaAllocator();
        var set = new ArenaSet<int>(arena);
        var enumerator = set.GetEnumerator();

        enumerator.Should().BeOfType<ArenaSet<int>.Enumerator>();
    }

    // ---- M6: cross-encoding span lookups must convert, and unsupported key types must throw ----

    [Fact]
    public void ArenaSet_OfUtf8String_ContainsCharSpan_ConvertsInsteadOfAlwaysFalse()
    {
        using var arena = new ArenaAllocator();
        var set = new ArenaSet<ArenaUtf8String>(arena);
        set.Add(ArenaUtf8String.Clone("hello", arena));

        set.Contains("hello".AsSpan()).Should().BeTrue();
        set.Contains("nope".AsSpan()).Should().BeFalse();
    }

    [Fact]
    public void ArenaSet_OfUtf16String_ContainsByteSpan_ConvertsInsteadOfAlwaysFalse()
    {
        using var arena = new ArenaAllocator();
        var set = new ArenaSet<ArenaUtf16String>(arena);
        set.Add(ArenaUtf16String.Clone("hello", arena));

        set.Contains(System.Text.Encoding.UTF8.GetBytes("hello")).Should().BeTrue();
    }

    [Fact]
    public void ArenaDictionary_NonStringKey_CharSpanLookup_ThrowsNotSupported()
    {
        using var arena = new ArenaAllocator();
        var dict = new ArenaDictionary<int, int>(arena);
        dict.TryAdd(1, 1);

        Action act = () => dict.ContainsKey("1".AsSpan());
        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void ArenaSet_NonStringElement_CharSpanLookup_ThrowsNotSupported()
    {
        using var arena = new ArenaAllocator();
        var set = new ArenaSet<int>(arena);
        set.Add(1);

        Action act = () => set.Contains("1".AsSpan());
        act.Should().Throw<NotSupportedException>();
    }

    // ---- M7: an existing key/item must not trigger growth, and Concatenate must respect its target arena ----

    [Fact]
    public void ArenaDictionary_TryAdd_ExistingKey_DoesNotTriggerGrowth()
    {
        using var arena = new ArenaAllocator();
        var dict = new ArenaDictionary<int, int>(arena, 4); // GrowThreshold == 2 at this capacity
        dict.TryAdd(1, 1);
        dict.TryAdd(2, 2); // count == 2 == GrowThreshold

        var bytesBefore = arena.AllocatedBytes;
        for (int i = 0; i < 100; i++)
        {
            dict.TryAdd(1, 999).Should().BeFalse();
        }

        arena.AllocatedBytes.Should().Be(bytesBefore, "re-adding an existing key must not grow the table");
    }

    [Fact]
    public void ArenaSet_Add_ExistingItem_DoesNotTriggerGrowth()
    {
        using var arena = new ArenaAllocator();
        var set = new ArenaSet<int>(arena, 4); // GrowThreshold == 2 at this capacity
        set.Add(1);
        set.Add(2); // count == 2 == GrowThreshold

        var bytesBefore = arena.AllocatedBytes;
        for (int i = 0; i < 100; i++)
        {
            set.Add(1).Should().BeFalse();
        }

        arena.AllocatedBytes.Should().Be(bytesBefore, "re-adding an existing item must not grow the table");
    }

    [Fact]
    public void ArenaUtf16String_Concatenate_WithEmptyOperand_ResultLivesInTargetArena()
    {
        using var arenaA = new ArenaAllocator();
        using var arenaB = new ArenaAllocator();

        var nonEmpty = ArenaUtf16String.Clone("hello", arenaA);
        var empty = default(ArenaUtf16String);

        var result = ArenaUtf16String.Concatenate(empty, nonEmpty, arenaB);

        result.ToString().Should().Be("hello");
        result.IsAlive(arenaB).Should().BeTrue("Concatenate documents that its result lives in the arena passed to it");
        result.IsAlive(arenaA).Should().BeFalse();
    }
}
