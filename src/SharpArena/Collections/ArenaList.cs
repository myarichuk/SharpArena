using System;
using System.Runtime.CompilerServices;
using SharpArena.Allocators;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SharpArena.Collections;

/// <summary>
/// Metadata describing the shared state of an <see cref="ArenaList{T}"/>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct ArenaListHeader
{
    /// <summary>
    /// Gets or sets the number of elements stored in the list.
    /// </summary>
    public int Count;

    /// <summary>
    /// Gets or sets the allocated capacity of the list.
    /// </summary>
    public int Capacity;

    /// <summary>
    /// Gets or sets a pointer to the unmanaged data buffer.
    /// </summary>
    public void* Data;
}


/// <summary>
/// A simple, arena-backed continuous list for unmanaged structs.
/// </summary>
/// <typeparam name="T">The unmanaged element type stored in the list.</typeparam>
[StructLayout(LayoutKind.Sequential)]
[System.Diagnostics.DebuggerDisplay("Length = {Length}, Capacity = {Capacity}")]
public unsafe struct ArenaList<T>
    where T : unmanaged
{
    private readonly ArenaAllocator _arena; // class reference – fine
    private readonly int _generation;
    internal ArenaListHeader* Header => _header;
    private ArenaListHeader* _header;

    /// <summary>
    /// Initializes a new instance of the <see cref="ArenaList{T}"/> struct.
    /// </summary>
    /// <param name="arena">Allocator providing storage.</param>
    /// <param name="initialCapacity">Initial number of items that can be stored without growing.</param>
    public ArenaList(ArenaAllocator arena, int initialCapacity = 16)
    {
        _arena = arena;
        _generation = arena.CurrentGeneration;
        
        if (initialCapacity <= 0)
        {
            initialCapacity = 1;
        }

        _header = (ArenaListHeader*)arena.Alloc((nuint)sizeof(ArenaListHeader), align: (nuint)IntPtr.Size);
        _header->Count = 0;
        _header->Capacity = initialCapacity;

        ulong byteCount = (ulong)(uint)initialCapacity * (ulong)sizeof(T);
        if (byteCount != (ulong)(nuint)byteCount)
        {
            throw new ArgumentOutOfRangeException(nameof(initialCapacity), "Initial capacity exceeds addressable memory.");
        }

        _header->Data = (T*)arena.Alloc(
            (nuint)initialCapacity * (nuint)sizeof(T),
            align: (nuint)UnsafeHelpers.AlignOf<T>());
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void CheckAliveThrowIfNot()
    {
        UnsafeHelpers.CheckAliveThrowIfNot(_arena, _generation, nameof(ArenaList<T>));
    }

    /// <summary>
    /// Gets the number of elements stored in the list.
    /// </summary>
    public int Length
    {
        get
        {
            CheckAliveThrowIfNot();
            return _header != null ? _header->Count : 0;
        }
    }

    /// <summary>
    /// Gets the allocated capacity of the list.
    /// </summary>
    public int Capacity
    {
        get
        {
            CheckAliveThrowIfNot();
            return _header != null ? _header->Capacity : 0;
        }
    }

    /// <summary>
    /// Gets a value indicating whether the list is empty.
    /// </summary>
    public bool IsEmpty
    {
        get
        {
            CheckAliveThrowIfNot();
            return _header == null || _header->Count == 0;
        }
    }

    /// <summary>
    /// Provides indexed access to the list items.
    /// </summary>
    /// <param name="index">The zero-based index of the item to access.</param>
    /// <returns>A reference to the item at the requested index.</returns>
    public ref T this[int index]
    {
        get
        {
            CheckAliveThrowIfNot();
            if ((uint)index >= (uint)_header->Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }
            return ref ((T*)_header->Data)[index];
        }
    }

    /// <summary>
    /// Appends a new element to the list, expanding the buffer if necessary.
    /// </summary>
    /// <param name="value">The value to add.</param>
    public void Add(in T value)
    {
        CheckAliveThrowIfNot();
        if (_header->Count >= _header->Capacity)
        {
            Grow();
        }

        ((T*)_header->Data)[_header->Count++] = value;
    }

    private void Grow()
    {
        if (_header->Capacity > int.MaxValue / 2)
        {
            throw new InvalidOperationException("ArenaList capacity overflow.");
        }

        var newCap = (nuint)_header->Capacity * 2;
        var newPtr = _arena.Alloc(
            newCap * (nuint)sizeof(T),
            align: (nuint)UnsafeHelpers.AlignOf<T>());

        ulong bytesToCopy = (ulong)(uint)_header->Count * (ulong)sizeof(T);
        ulong destBytes = (ulong)newCap * (ulong)sizeof(T);
        System.Buffer.MemoryCopy(_header->Data, newPtr, destBytes, bytesToCopy);

        _header->Data = newPtr;
        _header->Capacity = (int)newCap;
    }

    /// <summary>
    /// Resets the list to an empty state while preserving the allocated buffer.
    /// </summary>
    public void Reset()
    {
        CheckAliveThrowIfNot();
        // just in case :)
        if (_header == null)
        {
            return;
        }

        _header->Count = 0;
    }

    /// <summary>
    /// Gets a pointer to the raw unmanaged data of the list.
    /// </summary>
    public T* AsPtr
    {
        get
        {
            CheckAliveThrowIfNot();
            return (T*)_header->Data;
        }
    }

    /// <summary>
    /// Gets a writable span over the list's contents.
    /// </summary>
    public Span<T> Span => AsSpan();

    /// <summary>
    /// Provides a writable span view of the stored elements.
    /// </summary>
    /// <returns>A span referencing the list contents.</returns>
    public Span<T> AsSpan()
    {
        CheckAliveThrowIfNot();
        return new Span<T>((T*)_header->Data, _header->Count);
    }

    /// <summary>
    /// Provides a read-only span view of the stored elements.
    /// </summary>
    /// <returns>A read-only span referencing the list contents.</returns>
    public ReadOnlySpan<T> AsReadOnlySpan()
    {
        CheckAliveThrowIfNot();
        return new ReadOnlySpan<T>((T*)_header->Data, _header->Count);
    }
}
