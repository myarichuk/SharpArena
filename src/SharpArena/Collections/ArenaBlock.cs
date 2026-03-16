using System.Runtime.CompilerServices;
using SharpArena.Allocators;
using System.Collections;
using System.Diagnostics;
using System.Runtime.InteropServices;
// ReSharper disable UnusedMember.Global
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

namespace SharpArena.Collections;

/// <summary>
/// Describes a single arena-allocated block within an <see cref="ArenaBlockList{T}"/>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct ArenaBlock<T>
    where T : unmanaged
{
    /// <summary>
    /// Gets or sets the pointer to the element storage for this block.
    /// </summary>
    public T* Data;

    /// <summary>
    /// Gets or sets the number of elements written to the block.
    /// </summary>
    public nuint Count;

    /// <summary>
    /// Gets or sets the capacity of the block.
    /// </summary>
    public nuint Capacity;

    /// <summary>
    /// Gets or sets the pointer to the next block in the chain.
    /// </summary>
    public ArenaBlock<T>* Next;
}

/// <summary>
/// Metadata describing the shared state of an <see cref="ArenaBlockList{T}"/>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct ArenaBlockListHeader<T>
    where T : unmanaged
{
    /// <summary>
    /// Gets or sets the total number of elements stored across all blocks.
    /// </summary>
    public nuint TotalCount;
    /// <summary>
    /// Gets or sets the total allocated capacity across all blocks.
    /// </summary>
    public nuint TotalCapacity;
    /// <summary>
    /// Gets or sets the pointer to the current block where the next addition will occur.
    /// </summary>
    public ArenaBlock<T>* CurrentBlock;
}

/// <summary>
/// Provides a growable sequence of arena-backed blocks that stores unmanaged values without GC allocations.
/// </summary>
/// <typeparam name="T">The unmanaged element type.</typeparam>
[StructLayout(LayoutKind.Sequential)]
[System.Diagnostics.DebuggerDisplay("Count = {Count}, Capacity = {Capacity}")]
public unsafe struct ArenaBlockList<T> : IEnumerable<T>
    where T : unmanaged
{
    private const nuint DefaultBlockSize = 128;

    private readonly ArenaAllocator _arena;
    private readonly int _generation; //generation snapshot
    private readonly ArenaBlock<T>* _head;
    private ArenaBlockListHeader<T>* _header;

    /// <summary>
    /// Initializes a new instance of the <see cref="ArenaBlockList{T}"/> struct.
    /// </summary>
    /// <param name="arena">Allocator providing unmanaged storage.</param>
    /// <param name="blockSize">Initial block capacity.</param>
    public ArenaBlockList(ArenaAllocator arena, nuint blockSize = DefaultBlockSize)
    {
        _arena = arena;
        _generation = arena.CurrentGeneration;
        
        var firstBlock = CreateBlock(arena, blockSize);
        _head = firstBlock;

        _header = (ArenaBlockListHeader<T>*)arena.Alloc((nuint)sizeof(ArenaBlockListHeader<T>), align: (nuint)IntPtr.Size);
        _header->TotalCount = 0;
        _header->TotalCapacity = blockSize;
        _header->CurrentBlock = firstBlock;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void CheckAliveThrowIfNot()
    {
        UnsafeHelpers.CheckAliveThrowIfNot(_arena, _generation, nameof(ArenaBlockList<T>));
    }

    private static ArenaBlock<T>* CreateBlock(ArenaAllocator arena, nuint capacity)
    {
#if NET7_0_OR_GREATER
        nuint max = nuint.MaxValue;
#else
        nuint max = unchecked((nuint)ulong.MaxValue);
#endif
        if (capacity > max / (nuint)sizeof(T))
        {
            throw new OutOfMemoryException("Capacity exceeds maximum addressable memory.");
        }

        nuint headerSize = (nuint)sizeof(ArenaBlock<T>);
        nuint dataSize = capacity * (nuint)sizeof(T);

        if (max - headerSize < dataSize)
        {
            throw new OutOfMemoryException("Block size exceeds maximum addressable memory.");
        }

        byte* mem = (byte*)arena.Alloc(headerSize + dataSize,  (nuint)Unsafe.SizeOf<T>());
        var block = (ArenaBlock<T>*)mem;
        block->Data = (T*)(mem + headerSize);
        block->Count = 0;
        block->Capacity = capacity;
        block->Next = null;
        return block;
    }

    /// <summary>
    /// Gets the number of elements stored across all blocks.
    /// </summary>
    public nuint Count
    {
        get
        {
            CheckAliveThrowIfNot();
            return _header->TotalCount;
        }
    }

    /// <summary>
    /// Gets the total allocated capacity across all blocks.
    /// </summary>
    public nuint Capacity
    {
        get
        {
            CheckAliveThrowIfNot();
            return _header->TotalCapacity;
        }
    }

    /// <summary>
    /// Resets the list to an empty state without freeing allocated blocks.
    /// </summary>
    public void Reset()
    {
        CheckAliveThrowIfNot();
        for (var b = _head; b != null; b = b->Next)
        {
            b->Count = 0;
        }

        _header->CurrentBlock = _head;
        _header->TotalCount = 0;
    }

    /// <summary>
    /// Adds a value to the list, allocating a new block when the current block is full.
    /// </summary>
    /// <param name="value">The value to append.</param>
    public void Add(in T value)
    {
        CheckAliveThrowIfNot();
        var current = _header->CurrentBlock;
        if (current->Count >= current->Capacity)
        {
            var nextCapacity = current->Capacity * 2;
            var newBlock = CreateBlock(_arena, nextCapacity);
            current->Next = newBlock;
            _header->CurrentBlock = newBlock;
            _header->TotalCapacity += nextCapacity;
            current = newBlock;
        }

        current->Data[current->Count++] = value;
        _header->TotalCount++;
    }

    /// <summary>
    /// Returns an enumerator that iterates through the values stored in the block list.
    /// </summary>
    /// <returns>An enumerator over the list.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Enumerator GetEnumerator()
    {
        CheckAliveThrowIfNot();
        return new Enumerator(_head, _arena);
    }

    IEnumerator<T> IEnumerable<T>.GetEnumerator()
    {
        CheckAliveThrowIfNot();
        return new Enumerator(_head, _arena);
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        CheckAliveThrowIfNot();
        return new Enumerator(_head, _arena);
    }

    /// <summary>
    /// Provides a value-type enumerator for iterating over the block list contents.
    /// </summary>
    /// <summary>
    /// Provides a value-type enumerator for iterating over the block list contents.
    /// </summary>
    public struct Enumerator : IEnumerator<T>
    {
        private readonly ArenaAllocator _arena;
        private readonly int _generation;
        private readonly ArenaBlock<T>* _head;
        private ArenaBlock<T>* _block;
        private nuint _index;
        private T _current;

        internal Enumerator(ArenaBlock<T>* head, ArenaAllocator arena)
        {
            _arena = arena ?? throw new ArgumentNullException(nameof(arena));
            _generation = arena.CurrentGeneration;
            _head = head;
            _block = head;
            _index = 0;
            _current = default;
        }

        public T Current => _current;
        object IEnumerator.Current => _current!;

        public bool MoveNext()
        {
            ThrowIfArenaDead();

            if (_block == null)
                return false;

            while (_block != null)
            {
                if (_index < _block->Count)
                {
                    _current = _block->Data[_index++];
                    return true;
                }

                _block = _block->Next;
                _index = 0;
            }

            return false;
        }

        private void ThrowIfArenaDead()
        {
            if (_arena == null || _arena.CurrentGeneration != _generation)
            {
                throw new ObjectDisposedException(nameof(Enumerator), "Arena was reset or disposed");
            }
        }

        public void Reset()
        {
            ThrowIfArenaDead();
            _block = _head;
            _index = 0;
            _current = default;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            // Nothing to dispose
        }
    }
    /// <summary>
    /// Copies the list contents into a contiguous span allocated from the arena.
    /// </summary>
    /// <returns>A span that owns a contiguous copy of the stored values.</returns>
    public ReadOnlySpan<T> GetSpan()
    {
        CheckAliveThrowIfNot();
        var total = Count;
        if (total == 0)
        {
            return ReadOnlySpan<T>.Empty;
        }

        var buffer = (T*)_arena.Alloc(total * (nuint)sizeof(T));
        var dst = buffer;
        for (var block = _head; block != null; block = block->Next)
        {
            if (block->Count > 0)
            {
                var bytesToCopy = block->Count * (nuint)sizeof(T);
                System.Buffer.MemoryCopy(block->Data, dst, bytesToCopy, bytesToCopy);
                dst += block->Count;
            }
        }

        return new ReadOnlySpan<T>(buffer, (int)total);
    }
}
