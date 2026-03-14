using System.Runtime.CompilerServices;
using SharpArena.Allocators;
using System.Runtime.InteropServices;

namespace SharpArena.Collections;

/// <summary>
/// Describes the metadata shared by copies of an <see cref="ArenaPtrStack{T}"/>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct ArenaPtrStackHeader
{
    /// <summary>
    /// Gets or sets the number of pointers contained in the stack.
    /// </summary>
    public int Count;

    /// <summary>
    /// Gets or sets the capacity of the stack.
    /// </summary>
    public int Capacity;

    /// <summary>
    /// Gets or sets the pointer to the unmanaged array of entries.
    /// </summary>
    public void* Data; // points to a T* array
}

/// <summary>
/// A growable, non-allocating stack of pointers backed by an <see cref="ArenaAllocator"/>.
/// Safe to copy by value since metadata and buffer pointer are shared through the header.
/// </summary>
/// <typeparam name="T">Unmanaged element type (the stack stores pointers to this type).</typeparam>
[System.Diagnostics.DebuggerDisplay("Count = {Count}, Capacity = {Capacity}")]
public unsafe struct ArenaPtrStack<T>
    where T : unmanaged
{
    private readonly ArenaAllocator _arena;
    private readonly int _generation;
    private readonly ArenaPtrStackHeader* _header;

    /// <summary>
    /// Initializes a new instance of the <see cref="ArenaPtrStack{T}"/> struct.
    /// </summary>
    /// <param name="arena">The allocator providing storage.</param>
    /// <param name="initialCapacity">Initial pointer capacity of the stack.</param>
    public ArenaPtrStack(ArenaAllocator arena, int initialCapacity = 16)
    {
        if (initialCapacity <= 0)
        {
            initialCapacity = 1;
        }

        _arena = arena;
        _generation = arena.CurrentGeneration;

        _header = (ArenaPtrStackHeader*)arena.Alloc(
            (nuint)sizeof(ArenaPtrStackHeader),
            align: (nuint)IntPtr.Size);

        _header->Count = 0;
        _header->Capacity = initialCapacity;

        ulong byteCount = (ulong)(uint)initialCapacity * (ulong)sizeof(T*);
        if (byteCount != (ulong)(nuint)byteCount)
        {
            throw new ArgumentOutOfRangeException(nameof(initialCapacity), "Initial capacity exceeds addressable memory.");
        }

        _header->Data = arena.Alloc(
            (nuint)byteCount,
            align: (nuint)IntPtr.Size);
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void CheckAliveThrowIfNot()
    {
        UnsafeHelpers.CheckAliveThrowIfNot(_arena, _generation, nameof(ArenaPtrStack<T>));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowInvalidOperation(string msg)
        => throw new InvalidOperationException(msg);

    /// <summary>
    /// Gets a value indicating whether the stack is empty.
    /// </summary>
    public bool IsEmpty
    {
        get
        {
            CheckAliveThrowIfNot();
            return _header->Count == 0;
        }
    }

    /// <summary>
    /// Gets the number of items currently stored in the stack.
    /// </summary>
    public int Count
    {
        get
        {
            CheckAliveThrowIfNot();
            return _header->Count;
        }
    }

    /// <summary>
    /// Gets the total allocated capacity of the stack.
    /// </summary>
    public int Capacity
    {
        get
        {
            CheckAliveThrowIfNot();
            return _header->Capacity;
        }
    }

    /// <summary>
    /// Pushes a pointer onto the stack, growing the backing buffer as needed.
    /// </summary>
    /// <param name="value">The pointer to push.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Push(T* value)
    {
        CheckAliveThrowIfNot();
        if (_header->Count >= _header->Capacity)
        {
            Grow();
        }

        var data = (T**)_header->Data;
        data[_header->Count++] = value;
    }

    /// <summary>
    /// Removes and returns the pointer at the top of the stack.
    /// </summary>
    /// <returns>The pointer previously at the top of the stack.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the stack is empty.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T* Pop()
    {
        CheckAliveThrowIfNot();
        if (_header->Count == 0)
        {
            ThrowInvalidOperation("ArenaPtrStack underflow");
        }

        var data = (T**)_header->Data;
        return data[--_header->Count];
    }

    /// <summary>
    /// Returns the pointer at the top of the stack without removing it.
    /// </summary>
    /// <returns>The pointer at the top of the stack.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the stack is empty.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T* Peek()
    {
        CheckAliveThrowIfNot();
        if (_header->Count == 0)
        {
            ThrowInvalidOperation("ArenaPtrStack empty");
        }

        var data = (T**)_header->Data;
        return data[_header->Count - 1];
    }

    /// <summary>
    /// Clears the stack contents without releasing the backing buffer.
    /// </summary>
    public void Clear()
    {
        CheckAliveThrowIfNot();
        _header->Count = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Grow()
    {
        int oldCap = _header->Capacity;
        if (oldCap >= int.MaxValue)
        {
            ThrowInvalidOperation("ArenaPtrStack capacity overflow");
        }

        int newCap = oldCap > int.MaxValue / 2 ? int.MaxValue : oldCap * 2;
        ulong byteCount = (ulong)(uint)newCap * (ulong)sizeof(T*);
        ulong oldByteCount = (ulong)(uint)_header->Count * (ulong)sizeof(T*);

        if (byteCount != (ulong)(nuint)byteCount)
        {
            ThrowInvalidOperation("ArenaPtrStack capacity exceeds addressable memory");
        }

        var newPtr = _arena.Alloc((nuint)byteCount, align: (nuint)IntPtr.Size);

        Buffer.MemoryCopy(
            source: _header->Data,
            destination: newPtr,
            destinationSizeInBytes: byteCount,
            sourceBytesToCopy: oldByteCount);

        _header->Data = newPtr;
        _header->Capacity = newCap;
    }

}
