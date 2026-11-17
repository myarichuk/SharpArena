// Skip zero-initialising locals and stackalloc buffers. The library is all hot-path unsafe code;
// every stackalloc'd buffer here is written before it is read (encoders report `written`, and
// only that slice is consumed), so the implicit zeroing is pure overhead.
[module: System.Runtime.CompilerServices.SkipLocalsInit]

#if !NET5_0_OR_GREATER
namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.Module | AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface |
                    AttributeTargets.Constructor | AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Event,
        Inherited = false)]
    internal sealed class SkipLocalsInitAttribute : Attribute
    {
    }
}
#endif
