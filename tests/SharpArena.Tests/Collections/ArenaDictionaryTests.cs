using FluentAssertions;
using SharpArena.Allocators;
using SharpArena.Collections;
using Xunit;

namespace SharpArena.Tests.Collections;

public class ArenaDictionaryTests : IDisposable
{
    private readonly ArenaAllocator _arena = new();

    public void Dispose() => _arena.Dispose();

    [Fact]
    public void Add_NewEntry_IncrementsCountAndEnablesLookup()
    {
        var dict = new ArenaDictionary<int, int>(_arena);
        dict.Add(1, 100);
        
        dict.Count.Should().Be(1);
        dict.ContainsKey(1).Should().BeTrue();
        dict[1].Should().Be(100);
    }

    [Fact]
    public void Add_DuplicateKey_ThrowsArgumentException()
    {
        var dict = new ArenaDictionary<int, int>(_arena);
        dict.Add(1, 100);
        
        Action act = () => dict.Add(1, 200);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void TryAdd_DuplicateKey_ReturnsFalse()
    {
        var dict = new ArenaDictionary<int, int>(_arena);
        dict.TryAdd(1, 100).Should().BeTrue();
        dict.TryAdd(1, 200).Should().BeFalse();
    }

    [Fact]
    public void TryGetValue_ValidKey_ReturnsTrue()
    {
        var dict = new ArenaDictionary<int, int>(_arena);
        dict.Add(42, 1337);

        dict.TryGetValue(42, out int value).Should().BeTrue();
        value.Should().Be(1337);
    }

    [Fact]
    public void TryGetValue_InvalidKey_ReturnsFalse()
    {
        var dict = new ArenaDictionary<int, int>(_arena);
        dict.TryGetValue(99, out int value).Should().BeFalse();
    }

    [Fact]
    public void Indexer_Set_UpdatesExistingValue()
    {
        var dict = new ArenaDictionary<int, int>(_arena);
        dict[1] = 100;
        dict[1] = 200;

        dict.Count.Should().Be(1);
        dict[1].Should().Be(200);
    }

    [Fact]
    public void Grow_PreservesAllEntries()
    {
        var dict = new ArenaDictionary<int, int>(_arena, initialCapacity: 4);
        for (int i = 0; i < 100; i++)
        {
            dict.Add(i, i * 10);
        }

        dict.Count.Should().Be(100);
        for (int i = 0; i < 100; i++)
        {
            dict[i].Should().Be(i * 10);
        }
    }

    [Fact]
    public void KeysAndValues_Collections_Work()
    {
        var dict = new ArenaDictionary<int, int>(_arena);
        dict.Add(1, 10);
        dict.Add(2, 20);

        dict.Keys.Should().Contain(new[] { 1, 2 });
        dict.Values.Should().Contain(new[] { 10, 20 });
    }

    [Fact]
    public void ArenaString_AsKey_WorksCorrectly()
    {
        var dict = new ArenaDictionary<ArenaString, int>(_arena);
        var s1 = ArenaString.Clone("key1", _arena);
        var s2 = ArenaString.Clone("key2", _arena);

        dict.Add(s1, 1);
        dict.Add(s2, 2);

        dict[ArenaString.Clone("key1", _arena)].Should().Be(1); // Content equality check
        dict.ContainsKey(ArenaString.Clone("key2", _arena)).Should().BeTrue();
        dict.ContainsKey(ArenaString.Clone("key3", _arena)).Should().BeFalse();
    }

    [Fact]
    public void Clear_ResetsCountAndLookups()
    {
        var dict = new ArenaDictionary<int, int>(_arena);
        dict.Add(1, 100);
        dict.Clear();

        dict.Count.Should().Be(0);
        dict.ContainsKey(1).Should().BeFalse();
    }
}
