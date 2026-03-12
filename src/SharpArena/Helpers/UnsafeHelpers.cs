using System.Runtime.CompilerServices;
using SharpArena.Allocators;
using System.Runtime.CompilerServices;

namespace SharpArena.Helpers;

public class UnsafeHelpers
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int AlignOf<T>() where T : unmanaged
    {
        var size = Unsafe.SizeOf<T>();

        // assume at least pointer-size alignment (worst case bit over-align)
        var required = size < IntPtr.Size ? IntPtr.Size : size;
        return RoundUpToPowerOfTwo(required);
    }

    private static int RoundUpToPowerOfTwo(int value)
    {
        if (value <= 0)
        {
            return 1;
        }

        value--;
        value |= value >> 1;
        value |= value >> 2;
        value |= value >> 4;
        value |= value >> 8;
        value |= value >> 16;
        value++;
        return value;
    }
}
