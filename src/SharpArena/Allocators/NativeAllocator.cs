using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

#if DEBUG
using System.Collections.Concurrent;
using System.Diagnostics;
// ReSharper disable InconsistentNaming
#endif

namespace SharpArena.Allocators;

/// <summary>
/// Identifies the mechanism used to allocate unmanaged memory.
/// </summary>
public enum NativeAllocatorBackend
{
    /// <summary>
    /// Uses runtime-provided unmanaged allocation helpers.
    /// </summary>
    DotNetUnmanaged,

    /// <summary>
    /// Uses platform invoke to call OS-specific allocation APIs.
    /// </summary>
    PlatformInvoke,
}

/// <summary>
/// Describes the page protection applied to unmanaged memory.
/// </summary>
public enum MemoryProtectionMode
{
    /// <summary>
    /// No additional protection is applied.
    /// </summary>
    None,

    /// <summary>
    /// Marks the memory as read-only.
    /// </summary>
    ReadOnly,

    /// <summary>
    /// Marks the memory as inaccessible.
    /// </summary>
    NoAccess,
}

/// <summary>
/// Provides helpers for allocating and freeing unmanaged buffers with optional guard pages.
/// </summary>
public static unsafe class NativeAllocator
{
    private const ulong MagicValue = 0xDEADC0DECAFEBEEFUL;
    private const ulong FreedValue = 0xFEEDF00DDEADBEAFL;

    private static readonly nuint HeaderSize = (nuint)sizeof(AllocationHeader);
    private static readonly nuint PageSize = (nuint)Environment.SystemPageSize;

    static NativeAllocator()
    {
#if DEBUG
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            if (!_active.IsEmpty)
            {
                Console.Error.WriteLine($"[NativeAllocator] { _active.Count } allocation(s) not freed before process exit:");
                foreach (var kv in _active)
                {
                    var info = kv.Value;
                    Console.Error.WriteLine($"  -> Leak at 0x{kv.Key:X}, size {info.ReservedSize} bytes, backend {info.Backend}");
                }
            }

            Debug.Assert(_active.IsEmpty, "having _active.Count > 0 at process shutdown means we have a leak. THIS IS BAD!");
        };
#endif
    }

    private static bool IsWindowsPlatform()
        => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    // ConcurrentDictionary ensures async/thread safety
#if DEBUG
    private static readonly ConcurrentDictionary<nint, AllocationInfo> _active = new();

    private readonly struct AllocationInfo(
        nint rawPtr,
        nuint reservedSize,
        nuint guardPrefix,
        NativeAllocatorBackend backend)
    {
        public nint RawPtr { get; } = rawPtr;

        public nuint ReservedSize { get; } = reservedSize;

        // ReSharper disable once MemberCanBePrivate.Local
        public nuint GuardPrefix { get; } = guardPrefix;

        public NativeAllocatorBackend Backend { get; } = backend;

        // ReSharper disable once UnusedMember.Local
        public AllocationHeader* Header
            => (AllocationHeader*)((byte*)RawPtr + GuardPrefix);
    }
#endif

    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    private struct AllocationHeader
    {
        public ulong Magic;
        public nuint Size;
        public nuint ReservedSize;
        public nuint GuardPrefix;
        public nuint GuardSuffix;
        public NativeAllocatorBackend Backend;
    }

    /// <summary>
    /// Allocates unmanaged memory with optional guard pages and protection.
    /// </summary>
    /// <param name="size">The requested allocation size in bytes.</param>
    /// <param name="backend">The backend used to fulfill the allocation.</param>
    /// <param name="protection">Optional memory protection applied after allocation.</param>
    /// <returns>A pointer to the usable memory region, or <see langword="null"/> when <paramref name="size"/> is zero.</returns>
    /// <exception cref="OutOfMemoryException">Thrown when the allocation fails.</exception>
    public static void* Alloc(
        nuint size,
        NativeAllocatorBackend backend = NativeAllocatorBackend.PlatformInvoke,
        MemoryProtectionMode protection = MemoryProtectionMode.None)
    {
        if (size == 0)
        {
            return null;
        }

        if (size > nuint.MaxValue - HeaderSize)
        {
            throw new OutOfMemoryException("Native allocation failed due to integer overflow.");
        }

        nuint total;
        nuint alignedTotal;
        nuint guardPrefix = 0;
        nuint guardSuffix = 0;

        try
        {
            checked
            {
                total = size + HeaderSize;
                alignedTotal = total;

                if (backend is NativeAllocatorBackend.PlatformInvoke)
                {
                    alignedTotal = AlignUp(total, PageSize);
#if DEBUG
                    guardPrefix = PageSize;
                    guardSuffix = PageSize;
                    alignedTotal += guardPrefix + guardSuffix;
#endif
                }
            }
        }
        catch (OverflowException)
        {
            throw new OutOfMemoryException("Native allocation failed due to integer overflow.");
        }

        void* rawPtr = backend switch
        {
#if NET8_0_OR_GREATER
            NativeAllocatorBackend.DotNetUnmanaged => NativeMemory.Alloc(total),
#else
            NativeAllocatorBackend.DotNetUnmanaged => (void*)Marshal.AllocHGlobal(checked((nint)total)),
#endif
            _ when IsWindowsPlatform()
                => (void*)Native.VirtualAlloc(0, alignedTotal, Native.MEM_RESERVE | Native.MEM_COMMIT, Native.PAGE_READWRITE),
            _ => (void*)Native.mmap(IntPtr.Zero, alignedTotal, Native.PROT_READ | Native.PROT_WRITE,
                                    Native.MAP_PRIVATE | Native.MAP_ANONYMOUS, -1, 0),
        };

        if (backend is NativeAllocatorBackend.PlatformInvoke && IsMmapFailure(rawPtr))
        {
            rawPtr = null;
        }

        if (rawPtr is null)
        {
            throw new OutOfMemoryException("Native allocation failed");
        }

        var headerPtr = (AllocationHeader*)((byte*)rawPtr + guardPrefix);
        var hdr = headerPtr;
        hdr->Magic = MagicValue;
        hdr->Size = size;
        hdr->ReservedSize = alignedTotal;
        hdr->GuardPrefix = guardPrefix;
        hdr->GuardSuffix = guardSuffix;
        hdr->Backend = backend;

        var userPtr = (byte*)headerPtr + HeaderSize;

        if (protection != MemoryProtectionMode.None &&
            backend is NativeAllocatorBackend.PlatformInvoke)
        {
            ApplyProtection(userPtr, size, protection);
        }

#if DEBUG
        if (backend is NativeAllocatorBackend.PlatformInvoke)
        {
            ApplyGuard(rawPtr, guardPrefix, guardSuffix, alignedTotal);
        }
#endif

#if DEBUG
        var info = new AllocationInfo((nint)rawPtr, alignedTotal, guardPrefix, backend);
        _active[(nint)userPtr] = info;
#endif

        return userPtr;
    }

    /// <summary>
    /// Releases unmanaged memory previously allocated via <see cref="Alloc"/>.
    /// </summary>
    /// <param name="userPtr">Pointer returned by <see cref="Alloc"/>.</param>
    /// <param name="backend">Backend used when the memory was allocated.</param>
    /// <exception cref="InvalidOperationException">Thrown when the pointer is invalid or the backend mismatches.</exception>
    public static void Free(
        void* userPtr,
        NativeAllocatorBackend backend = NativeAllocatorBackend.PlatformInvoke)
    {
        if (userPtr is null)
        {
            return;
        }

        var header = (AllocationHeader*)((byte*)userPtr - HeaderSize);

#if DEBUG
        var key = (nint)userPtr;

        if (!_active.TryRemove(key, out var info))
        {
            throw new InvalidOperationException("Double free or foreign pointer detected.");
        }

        var rawPtr = info.RawPtr;
        var reservedSize = info.ReservedSize;
        var expectedBackend = info.Backend;
#else
#if !DEBUG
        if (backend is NativeAllocatorBackend.PlatformInvoke && IsPointerFreed(userPtr))
        {
            throw new InvalidOperationException("Double free or foreign pointer detected.");
        }
#endif
        nint rawPtr = 0;
        nuint reservedSize = 0;
        NativeAllocatorBackend expectedBackend = backend;

        try
        {
            var guardPrefix = header->GuardPrefix;
            rawPtr = (nint)((byte*)header - guardPrefix);
            reservedSize = header->ReservedSize;
            expectedBackend = header->Backend;
        }
        catch (AccessViolationException)
        {
            throw new InvalidOperationException("Foreign pointer detected.");
        }
#endif

        try
        {
            if (header->Magic != MagicValue)
            {
                throw new InvalidOperationException("Foreign pointer detected.");
            }

            header->Magic = FreedValue;
        }
        catch (AccessViolationException)
        {
            throw new InvalidOperationException("Foreign pointer detected.");
        }

        if (backend != expectedBackend)
        {
            throw new InvalidOperationException("Allocator backend mismatch.");
        }

        switch (expectedBackend)
        {
            case NativeAllocatorBackend.DotNetUnmanaged:
#if NET8_0_OR_GREATER
                NativeMemory.Free((void*)rawPtr);
#else
                Marshal.FreeHGlobal(rawPtr);
#endif
                return;

            case NativeAllocatorBackend.PlatformInvoke:
                if (IsWindowsPlatform())
                {
                    if (!Native.VirtualFree(rawPtr, 0, Native.MEM_RELEASE))
                    {
                        ThrowLastError("VirtualFree failed");
                    }
                }
                else if (Native.munmap(rawPtr, reservedSize) != 0)
                {
                    ThrowLastError("munmap failed");
                }
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(backend));
        }
    }

    private static bool IsMmapFailure(void* ptr)
        => !IsWindowsPlatform() && (nint)ptr == -1;

    private static void AlignToPage(void* ptr, nuint length, out void* alignedPtr, out nuint alignedLength)
    {
        var address = (nuint)ptr;
        var start = AlignDown(address, PageSize);
        var end = AlignUp(address + length, PageSize);
        alignedPtr = (void*)start;
        alignedLength = end - start;
    }

    private static nuint AlignUp(nuint value, nuint alignment)
    {
        if (alignment == 0)
        {
            return value;
        }

        var remainder = value % alignment;
        return remainder == 0 ? value : checked(value + (alignment - remainder));
    }

    private static nuint AlignDown(nuint value, nuint alignment)
    {
        if (alignment == 0)
        {
            return value;
        }

        var remainder = value % alignment;
        return value - remainder;
    }

#if !DEBUG
    private static bool IsPointerFreed(void* userPtr)
    {
        if (IsWindowsPlatform())
        {
            var querySize = (nuint)Unsafe.SizeOf<Native.MEMORY_BASIC_INFORMATION>();
            if (Native.VirtualQuery((nint)userPtr, out var info, querySize) == 0)
            {
                ThrowLastError("VirtualQuery failed");
            }

            return info.State == Native.MEM_FREE;
        }

        var pageSize = PageSize;
        var pageBase = (nint)AlignDown((nuint)userPtr, pageSize);

        Span<byte> vec = stackalloc byte[1];
        if (Native.mincore((IntPtr)pageBase, pageSize, ref MemoryMarshal.GetReference(vec)) == 0)
        {
            return false;
        }

        var errno = Marshal.GetLastWin32Error();
        if (errno == Native.ENOMEM)
        {
            return true;
        }

        ThrowLastError("mincore failed");
        return true;
    }
#endif

#if DEBUG
    private static void ApplyGuard(void* basePtr, nuint guardPrefix, nuint guardSuffix, nuint total)
    {
        if (guardPrefix == 0 && guardSuffix == 0)
        {
            return;
        }

        if (guardPrefix != 0)
        {
            ProtectGuard(basePtr, guardPrefix);
        }

        if (guardSuffix != 0)
        {
            var suffixPtr = (byte*)basePtr + total - guardSuffix;
            ProtectGuard(suffixPtr, guardSuffix);
        }
    }

    private static void ProtectGuard(void* ptr, nuint length)
    {
        if (length == 0)
        {
            return;
        }

        if (IsWindowsPlatform())
        {
            if (!Native.VirtualProtect((nint)ptr, length, Native.PAGE_NOACCESS, out _))
            {
                ThrowLastError("VirtualProtect guard failed");
            }
        }
        else if (Native.mprotect((IntPtr)ptr, length, Native.PROT_NONE) != 0)
        {
            ThrowLastError("mprotect guard failed");
        }
    }
#endif

    /// <summary>
    /// Applies page protection to an existing allocation.
    /// </summary>
    /// <param name="ptr">Pointer to the memory region.</param>
    /// <param name="size">Number of bytes to protect.</param>
    /// <param name="mode">Desired protection mode.</param>
    public static void ApplyProtection(void* ptr, nuint size, MemoryProtectionMode mode)
    {
        if (ptr is null || size == 0)
        {
            return;
        }

        AlignToPage(ptr, size, out var alignedPtr, out var alignedLength);

        var header = (AllocationHeader*)((byte*)ptr - HeaderSize);

        if (header->GuardPrefix != 0 || header->GuardSuffix != 0)
        {
            var start = (nuint)alignedPtr;
            var end = start + alignedLength;
            var basePtr = (nuint)((byte*)header - header->GuardPrefix);
            var userStart = basePtr + header->GuardPrefix;
            var userEnd = basePtr + header->ReservedSize - header->GuardSuffix;

            if (start < userStart)
            {
                var delta = userStart - start;
                if (delta >= alignedLength)
                {
                    return;
                }

                start = userStart;
                alignedPtr = (void*)start;
                alignedLength -= delta;
                end = start + alignedLength;
            }

            if (end > userEnd)
            {
                var delta = end - userEnd;
                if (delta >= alignedLength)
                {
                    return;
                }

                alignedLength -= delta;
            }

            if (alignedLength == 0)
            {
                return;
            }
        }

        if (IsWindowsPlatform())
        {
            var prot = mode switch
            {
                MemoryProtectionMode.ReadOnly => Native.PAGE_READONLY,
                MemoryProtectionMode.NoAccess => Native.PAGE_NOACCESS,
                _ => Native.PAGE_READWRITE,
            };
            if (!Native.VirtualProtect((nint)alignedPtr, alignedLength, prot, out _))
            {
                ThrowLastError("VirtualProtect failed");
            }
        }
        else
        {
            var prot = mode switch
            {
                MemoryProtectionMode.ReadOnly => Native.PROT_READ,
                MemoryProtectionMode.NoAccess => Native.PROT_NONE,
                _ => Native.PROT_READ | Native.PROT_WRITE,
            };
            if (Native.mprotect((IntPtr)alignedPtr, alignedLength, prot) != 0)
            {
                ThrowLastError("mprotect failed");
            }
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowLastError(string msg)
        => throw new InvalidOperationException($"{msg} (errno {Marshal.GetLastWin32Error()})");

#if DEBUG
    /// <summary>
    /// Gets the number of active allocations tracked in debug builds.
    /// </summary>
    public static int ActiveCount => _active.Count;
#endif
}



internal static partial class Native
{
    private const string Kernel32 = "kernel32.dll";

    // Win32 constants
    public const uint MEM_COMMIT = 0x1000, MEM_RESERVE = 0x2000, MEM_RELEASE = 0x8000, MEM_FREE = 0x10000;
    public const uint PAGE_READWRITE = 0x04, PAGE_READONLY = 0x02, PAGE_NOACCESS = 0x01;

    // POSIX constants
    public const int PROT_NONE = 0, PROT_READ = 1, PROT_WRITE = 2;
    public const int MAP_PRIVATE = 2, MAP_ANONYMOUS = 0x20;
    public const int ENOMEM = 12;
    
#if NET7_0_OR_GREATER
    [LibraryImport(Kernel32, SetLastError = true)]
    public static partial nint VirtualAlloc(nint lpAddress, nuint dwSize, uint flAllocationType, uint flProtect);

    [LibraryImport(Kernel32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool VirtualFree(nint lpAddress, nuint dwSize, uint dwFreeType);

    [LibraryImport(Kernel32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool VirtualProtect(nint lpAddress, nuint dwSize, uint flNewProtect, out uint lpflOldProtect);

    [LibraryImport(Kernel32, SetLastError = true)]
    public static partial nuint VirtualQuery(nint lpAddress, out MEMORY_BASIC_INFORMATION lpBuffer, nuint dwLength);
#else
    [DllImport(Kernel32, SetLastError = true, EntryPoint = "VirtualAlloc")]
    public static extern nint VirtualAlloc(nint lpAddress, nuint dwSize, uint flAllocationType, uint flProtect);

    [DllImport(Kernel32, SetLastError = true, EntryPoint = "VirtualFree")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool VirtualFree(nint lpAddress, nuint dwSize, uint dwFreeType);

    [DllImport(Kernel32, SetLastError = true, EntryPoint = "VirtualProtect")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool VirtualProtect(nint lpAddress, nuint dwSize, uint flNewProtect, out uint lpflOldProtect);

    [DllImport(Kernel32, SetLastError = true, EntryPoint = "VirtualQuery")]
    public static extern nuint VirtualQuery(nint lpAddress, out MEMORY_BASIC_INFORMATION lpBuffer, nuint dwLength);
#endif

    [StructLayout(LayoutKind.Sequential)]
    public struct MEMORY_BASIC_INFORMATION
    {
        public nint BaseAddress;
        public nint AllocationBase;
        public uint AllocationProtect;
        public nuint RegionSize;
        public uint State;
        public uint Protect;
        public uint Type;
    }

#if NET7_0_OR_GREATER
    [LibraryImport("libc", SetLastError = true)]
    public static partial IntPtr mmap(IntPtr addr, nuint length, int prot, int flags, int fd, long offset);

    [LibraryImport("libc", SetLastError = true)]
    public static partial int munmap(IntPtr addr, nuint length);

    [LibraryImport("libc", SetLastError = true)]
    public static partial int mprotect(IntPtr addr, nuint len, int prot);

    [LibraryImport("libc", SetLastError = true)]
    public static partial int mincore(IntPtr addr, nuint length, ref byte vec);
#else
    [DllImport("libc", SetLastError = true, EntryPoint = "mmap")]
    public static extern IntPtr mmap(IntPtr addr, nuint length, int prot, int flags, int fd, long offset);

    [DllImport("libc", SetLastError = true, EntryPoint = "munmap")]
    public static extern int munmap(IntPtr addr, nuint length);

    [DllImport("libc", SetLastError = true, EntryPoint = "mprotect")]
    public static extern int mprotect(IntPtr addr, nuint len, int prot);

    [DllImport("libc", SetLastError = true, EntryPoint = "mincore")]
    public static extern int mincore(IntPtr addr, nuint length, ref byte vec);
#endif
}
