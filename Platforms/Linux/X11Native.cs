using System.Runtime.InteropServices;

namespace WindowSnapper.Platforms.Linux;

internal static class X11Native
{
    private const string X11 = "libX11.so.6";

    [DllImport(X11)]
    public static extern IntPtr XOpenDisplay(IntPtr displayName);

    [DllImport(X11)]
    public static extern int XCloseDisplay(IntPtr display);

    [DllImport(X11)]
    public static extern IntPtr XDefaultRootWindow(IntPtr display);

    [DllImport(X11, CharSet = CharSet.Ansi)]
    public static extern IntPtr XInternAtom(IntPtr display, string atomName, bool onlyIfExists);

    [DllImport(X11)]
    public static extern int XGetWindowProperty(
        IntPtr display,
        IntPtr window,
        IntPtr property,
        IntPtr longOffset,
        IntPtr longLength,
        bool delete,
        IntPtr requestedType,
        out IntPtr actualType,
        out int actualFormat,
        out nuint itemCount,
        out nuint bytesAfter,
        out IntPtr propertyData);

    [DllImport(X11)]
    public static extern int XFetchName(IntPtr display, IntPtr window, out IntPtr windowName);

    [DllImport(X11)]
    public static extern int XQueryTree(
        IntPtr display,
        IntPtr window,
        out IntPtr rootReturn,
        out IntPtr parentReturn,
        out IntPtr childrenReturn,
        out uint childCount);

    [DllImport(X11)]
    public static extern int XGetGeometry(
        IntPtr display,
        IntPtr drawable,
        out IntPtr rootReturn,
        out int x,
        out int y,
        out uint width,
        out uint height,
        out uint borderWidth,
        out uint depth);

    [DllImport(X11)]
    public static extern int XFree(IntPtr data);
}
