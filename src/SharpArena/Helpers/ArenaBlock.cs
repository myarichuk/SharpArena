using System.Runtime.CompilerServices;
using SharpArena.Allocators;
using System.Collections;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace SharpArena.Helpers;

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
/// Provides a growable sequence of arena-backed blocks that stores unmanaged values without GC allocations.
/// </summary>
/// <typeparam name="T">The unmanaged element type.</typeparam>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct ArenaBlockList<T> : IEnumerable<T>
    where T : unmanaged
{
    private const nuint DefaultBlockSize = 128;

    private readonly ArenaAllocator _arena;
    private readonly ArenaBlock<T>* _head;
    private ArenaBlock<T>* _current;

    /// <summary>
    /// Initializes a new instance of the <see cref="ArenaBlockList{T}"/> struct.
    /// </summary>
    /// <param name="arena">Allocator providing unmanaged storage.</param>
    /// <param name="blockSize">Initial block capacity.</param>
    public ArenaBlockList(ArenaAllocator arena, nuint blockSize = DefaultBlockSize)
    {
        _arena = arena;
        _head = _current = CreateBlock(arena, blockSize);
    }

    private static ArenaBlock<T>* CreateBlock(ArenaAllocator arena, nuint capacity)
    {
        nuint headerSize = (nuint)sizeof(ArenaBlock<T>);
        nuint dataSize = capacity * (nuint)sizeof(T);

        byte* mem = (byte*)arena.Alloc(headerSize + dataSize,  (nuint)Unsafe.SizeOf<T>());
        var block = (ArenaBlock<T>*)mem;
        block->Data = (T*)(mem + headerSize);
        block->Count = 0;
        block->Capacity = capacity;
        block->Next = null;
        return block;
    }

    // TODO: cache in memory - we update it any way when adding

    /// <summary>
    /// Gets the number of elements stored across all blocks.
    /// </summary>
    public nuint Count
    {
        get
        {
            nuint total = 0;
            for (var b = _head; b != null; b = b->Next)
            {
                total += b->Count;
            }

            return total;
        }
    }

    /// <summary>
    /// Gets the total allocated capacity across all blocks.
    /// </summary>
    public nuint Capacity
    {
        get
        {
            nuint total = 0;
            for (var b = _head; b != null; b = b->Next)
            {
                total += b->Capacity;
            }

            return total;
        }
    }

    /// <summary>
    /// Resets the list to an empty state without freeing allocated blocks.
    /// </summary>
    public void Reset()
    {
        for (var b = _head; b != null; b = b->Next)
        {
            b->Count = 0;
        }

        _current = _head;
    }

    /// <summary>
    /// Adds a value to the list, allocating a new block when the current block is full.
    /// </summary>
    /// <param name="value">The value to append.</param>
    public void Add(in T value)
    {
        if (_current->Count >= _current->Capacity)
        {
            var newBlock = CreateBlock(_arena, _current->Capacity * 2);
            _current->Next = newBlock;
            _current = newBlock;
        }

        _current->Data[_current->Count++] = value;
    }

    /// <summary>
    /// Returns an enumerator that iterates through the values stored in the block list.
    /// </summary>
    /// <returns>An enumerator over the list.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Enumerator GetEnumerator() => new(_head);

    IEnumerator<T> IEnumerable<T>.GetEnumerator() => new Enumerator(_head);

    IEnumerator IEnumerable.GetEnumerator() => new Enumerator(_head);

    /// <summary>
    /// Provides a value-type enumerator for iterating over the block list contents.
    /// </summary>
    public struct Enumerator(ArenaBlock<T>* head) : IEnumerator<T>
    {
        private readonly ArenaBlock<T>* _head = head;
        private ArenaBlock<T>* _block = head;
        private nuint _index = 0;
        private T _current = default;

        /// <inheritdoc />
        public T Current => _current;

        /// <inheritdoc/>
        object IEnumerator.Current => _current!;

        /// <inheritdoc />
        public bool MoveNext()
        {
            if (_block == null)
            {
                return false;
            }

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

        /// <inheritdoc/>
        public void Reset()
        {
            _block = _head;
            _index = 0;
            _current = default;
        }

        /// <inheritdoc />
        public void Dispose() { }
    }

    /// <summary>
    /// Copies the list contents into a contiguous span allocated from the arena.
    /// </summary>
    /// <returns>A span that owns a contiguous copy of the stored values.</returns>
    public ReadOnlySpan<T> GetSpan()
    {
        var total = Count;
        if (total == 0)
        {
            return ReadOnlySpan<T>.Empty;
        }

        var buffer = (T*)_arena.Alloc(total * (nuint)sizeof(T));
        var dst = buffer;
        for (var block = _head; block != null; block = block->Next)
        {
            for (nuint i = 0; i < block->Count; i++)
            {
                *dst++ = block->Data[i];
            }
        }

        return new ReadOnlySpan<T>(buffer, (int)total);
    }
}