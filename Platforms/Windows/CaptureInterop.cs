using System.Runtime.InteropServices;
using Windows.Graphics.Capture;
using WinRT;

namespace WindowSnapper.Platforms.Windows;

internal static class CaptureInterop
{
    private static readonly Guid GraphicsCaptureItemId = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");

    [ComImport]
    [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        [PreserveSig]
        int CreateForWindow(IntPtr window, in Guid iid, out IntPtr result);

        [PreserveSig]
        int CreateForMonitor(IntPtr monitor, in Guid iid, out IntPtr result);
    }

    public static GraphicsCaptureItem CreateForWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            throw new ArgumentException("Window handle cannot be zero.", nameof(hwnd));

        var interop = GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>();
        var result = interop.CreateForWindow(hwnd, GraphicsCaptureItemId, out var itemPtr);
        Marshal.ThrowExceptionForHR(result);

        if (itemPtr == IntPtr.Zero)
            throw new InvalidOperationException("Windows Graphics Capture returned a null item.");

        try
        {
            return GraphicsCaptureItem.FromAbi(itemPtr);
        }
        finally
        {
            Marshal.Release(itemPtr);
        }
    }
}
