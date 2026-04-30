using System.Runtime.InteropServices;
using System.Text;
using FluentAssertions;
using SharpArena.Allocators;
using SharpArena.Collections;
using Xunit;

namespace SharpArena.Tests.Collections;

[StructLayout(LayoutKind.Sequential)]
public struct PaddedKey : IEquatable<PaddedKey>
{
    public byte A;
    public int B; // padding exists between A and B
    public bool Equals(PaddedKey other) => A == other.A && B == other.B;
    public override int GetHashCode() => HashCode.Combine(A, B);
}

public class ArenaDictionaryTests : IDisposable
{
    private readonly ArenaAllocator _arena = new();

    public void Dispose() => _arena.Dispose();

    [Fact]
    public void PaddedStructHashing_IgnoresPadding()
    {
        var dict = new ArenaDictionary<PaddedKey, int>(_arena);

        var key1 = new PaddedKey { A = 1, B = 2 };
        dict.Add(key1, 42);
        dict.ContainsKey(key1).Should().BeTrue();
    }

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
    public void ArenaUtf16String_AsKey_WorksCorrectly()
    {
        var dict = new ArenaDictionary<ArenaUtf16String, int>(_arena);
        var s1 = ArenaUtf16String.Clone("key1", _arena);
        var s2 = ArenaUtf16String.Clone("key2", _arena);

        dict.Add(s1, 1);
        dict.Add(s2, 2);

        dict[ArenaUtf16String.Clone("key1", _arena)].Should().Be(1); // Content equality check
        dict.ContainsKey(ArenaUtf16String.Clone("key2", _arena)).Should().BeTrue();
        dict.ContainsKey(ArenaUtf16String.Clone("key3", _arena)).Should().BeFalse();
    }

    [Fact]
    public void ContainsKey_Utf8ByteSpan_Works()
    {
        var dict = new ArenaDictionary<ArenaUtf8String, int>(_arena);
        var key = ArenaUtf8String.Clone("test", _arena);
        dict.Add(key, 123);

        ReadOnlySpan<byte> query = "test"u8;
        dict.ContainsKey(query).Should().BeTrue();
    }

    [Fact]
    public void TryGetValue_Utf8ByteSpan_Works()
    {
        var dict = new ArenaDictionary<ArenaUtf8String, int>(_arena);
        var key = ArenaUtf8String.Clone("test", _arena);
        dict.Add(key, 123);

        ReadOnlySpan<byte> query = "test"u8;
        dict.TryGetValue(query, out var val).Should().BeTrue();
        val.Should().Be(123);
    }

    [Fact]
    public void ContainsKey_CharSpan_OnUtf8Dict_Works()
    {
        var dict = new ArenaDictionary<ArenaUtf8String, int>(_arena);
        dict.Add(ArenaUtf8String.Clone("hello", _arena), 99);

        dict.ContainsKey("hello".AsSpan()).Should().BeTrue();
    }

    [Fact]
    public void TryGetValue_CharSpan_OnUtf8Dict_Works()
    {
        var dict = new ArenaDictionary<ArenaUtf8String, int>(_arena);
        dict.Add(ArenaUtf8String.Clone("hello", _arena), 99);

        dict.TryGetValue("hello".AsSpan(), out var val).Should().BeTrue();
        val.Should().Be(99);
    }

    [Fact]
    public void TryGetValue_LargeCharSpan_OnUtf8Dict_Works()
    {
        var dict = new ArenaDictionary<ArenaUtf8String, int>(_arena);
        string largeKey = new string('a', 600);
        dict.Add(ArenaUtf8String.Clone(largeKey, _arena), 1234);

        dict.TryGetValue(largeKey.AsSpan(), out var val).Should().BeTrue();
        val.Should().Be(1234);
    }

    [Fact]
    public void ContainsKey_ByteSpan_OnUtf16Dict_Works()
    {
        var dict = new ArenaDictionary<ArenaUtf16String, int>(_arena);
        dict.Add(ArenaUtf16String.Clone("world", _arena), 77);

        ReadOnlySpan<byte> query = "world"u8;
        dict.ContainsKey(query).Should().BeTrue();
    }

    [Fact]
    public void TryGetValue_ByteSpan_OnUtf16Dict_Works()
    {
        var dict = new ArenaDictionary<ArenaUtf16String, int>(_arena);
        dict.Add(ArenaUtf16String.Clone("world", _arena), 77);

        ReadOnlySpan<byte> query = "world"u8;
        dict.TryGetValue(query, out var val).Should().BeTrue();
        val.Should().Be(77);
    }

    [Fact]
    public void TryGetValue_LargeByteSpan_OnUtf16Dict_Works()
    {
        var dict = new ArenaDictionary<ArenaUtf16String, int>(_arena);
        string largeKey = new string('b', 600);
        dict.Add(ArenaUtf16String.Clone(largeKey, _arena), 5678);

        ReadOnlySpan<byte> query = Encoding.UTF8.GetBytes(largeKey);
        dict.TryGetValue(query, out var val).Should().BeTrue();
        val.Should().Be(5678);
    }

    [Fact]
    public void ContainsKey_LargeByteSpan_OnUtf16Dict_Works()
    {
        var dict = new ArenaDictionary<ArenaUtf16String, int>(_arena);
        string largeKey = new string('c', 600);
        dict.Add(ArenaUtf16String.Clone(largeKey, _arena), 999);

        ReadOnlySpan<byte> query = Encoding.UTF8.GetBytes(largeKey);
        dict.ContainsKey(query).Should().BeTrue();
    }

    [Fact]
    public void StressTest_CrossEncoding_LargeStrings()
    {
        var dictUtf8 = new ArenaDictionary<ArenaUtf8String, int>(_arena);
        var dictUtf16 = new ArenaDictionary<ArenaUtf16String, int>(_arena);
        
        for (int i = 0; i < 100; i++)
        {
            string key = new string((char)('a' + (i % 26)), 513 + i);
            dictUtf8.Add(ArenaUtf8String.Clone(key, _arena), i);
            dictUtf16.Add(ArenaUtf16String.Clone(key, _arena), i);
        }

        for (int i = 0; i < 100; i++)
        {
            string key = new string((char)('a' + (i % 26)), 513 + i);
            
            // Char -> Utf8
            dictUtf8.TryGetValue(key.AsSpan(), out var val1).Should().BeTrue();
            val1.Should().Be(i);
            
            // Byte -> Utf16
            ReadOnlySpan<byte> keyBytes = Encoding.UTF8.GetBytes(key);
            dictUtf16.TryGetValue(keyBytes, out var val2).Should().BeTrue();
            val2.Should().Be(i);
        }
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
