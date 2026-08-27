using System.Runtime.InteropServices;
using SharpDX.Direct3D11;

namespace WindowSnapper.Platforms.Windows;

internal static class Direct3D11Native
{
    // Bypass SharpDX here; its unmap wrapper is unreliable on a few drivers.
    private const int UnmapVtableSlot = 15;

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void UnmapDelegate(IntPtr context, IntPtr resource, uint subresource);

    public static void Unmap(DeviceContext context, Resource resource, int subresource)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(resource);

        var contextPtr = context.NativePointer;
        var resourcePtr = resource.NativePointer;
        if (contextPtr == IntPtr.Zero || resourcePtr == IntPtr.Zero)
            throw new InvalidOperationException("Direct3D resource is no longer valid.");

        var vtable = Marshal.ReadIntPtr(contextPtr);
        if (vtable == IntPtr.Zero)
            throw new InvalidOperationException("Direct3D device context has no COM vtable.");

        var unmapPtr = Marshal.ReadIntPtr(vtable, UnmapVtableSlot * IntPtr.Size);
        if (unmapPtr == IntPtr.Zero)
            throw new InvalidOperationException("ID3D11DeviceContext::Unmap is unavailable.");

        var unmap = Marshal.GetDelegateForFunctionPointer<UnmapDelegate>(unmapPtr);
        unmap(contextPtr, resourcePtr, unchecked((uint)subresource));
    }
}
