using System.Runtime.InteropServices;
using System.Text;
using WindowSnapper.Models;

namespace WindowSnapper.Platforms.Linux;

internal static class LinuxX11WindowFinder
{
    private const int Success = 0;

    public static IReadOnlyList<WindowInfo> GetOpenWindows()
    {
        var display = X11Native.XOpenDisplay(IntPtr.Zero);
        if (display == IntPtr.Zero)
            return Array.Empty<WindowInfo>();

        try
        {
            var root = X11Native.XDefaultRootWindow(display);
            var handles = ReadClientList(display, root);
            if (handles.Count == 0)
                handles = QueryChildren(display, root);

            var result = new List<WindowInfo>(handles.Count);
            foreach (var handle in handles.Distinct())
            {
                if (!TryGetGeometry(display, handle))
                    continue;

                var title = ReadWindowTitle(display, handle);
                if (string.IsNullOrWhiteSpace(title))
                    continue;

                var pid = ReadWindowPid(display, handle);
                var processName = ReadProcessName(pid);
                result.Add(new WindowInfo(handle, title.Trim(), processName, pid));
            }

            return result
                .OrderBy(w => w.ProcessName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(w => w.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        finally
        {
            X11Native.XCloseDisplay(display);
        }
    }

    private static List<nint> ReadClientList(IntPtr display, IntPtr root)
    {
        var atom = X11Native.XInternAtom(display, "_NET_CLIENT_LIST", true);
        if (atom == IntPtr.Zero)
            return new List<nint>();

        if (!TryGetProperty(display, root, atom, out var data, out var format, out var count))
            return new List<nint>();

        try
        {
            if (format != 32 || data == IntPtr.Zero)
                return new List<nint>();

            var result = new List<nint>((int)Math.Min((nuint)int.MaxValue, count));
            for (nuint i = 0; i < count; i++)
                result.Add(Marshal.ReadIntPtr(data, checked((int)i * IntPtr.Size)));
            return result;
        }
        finally
        {
            if (data != IntPtr.Zero)
                X11Native.XFree(data);
        }
    }

    private static List<nint> QueryChildren(IntPtr display, IntPtr root)
    {
        if (X11Native.XQueryTree(display, root, out _, out _, out var children, out var count) == 0
            || children == IntPtr.Zero)
        {
            return new List<nint>();
        }

        try
        {
            var result = new List<nint>((int)count);
            for (var i = 0; i < count; i++)
                result.Add(Marshal.ReadIntPtr(children, checked((int)i * IntPtr.Size)));
            return result;
        }
        finally
        {
            X11Native.XFree(children);
        }
    }

    private static string ReadWindowTitle(IntPtr display, IntPtr window)
    {
        var netWmName = X11Native.XInternAtom(display, "_NET_WM_NAME", true);
        if (netWmName != IntPtr.Zero && TryGetProperty(display, window, netWmName, out var data, out var format, out var count))
        {
            try
            {
                if (data != IntPtr.Zero && format == 8 && count > 0 && count <= int.MaxValue)
                {
                    var bytes = new byte[(int)count];
                    Marshal.Copy(data, bytes, 0, bytes.Length);
                    return Encoding.UTF8.GetString(bytes).TrimEnd('\0');
                }
            }
            finally
            {
                if (data != IntPtr.Zero)
                    X11Native.XFree(data);
            }
        }

        if (X11Native.XFetchName(display, window, out var name) == 0 || name == IntPtr.Zero)
            return string.Empty;

        try
        {
            return Marshal.PtrToStringAnsi(name) ?? string.Empty;
        }
        finally
        {
            X11Native.XFree(name);
        }
    }

    private static int ReadWindowPid(IntPtr display, IntPtr window)
    {
        var atom = X11Native.XInternAtom(display, "_NET_WM_PID", true);
        if (atom == IntPtr.Zero || !TryGetProperty(display, window, atom, out var data, out var format, out var count))
            return 0;

        try
        {
            if (data == IntPtr.Zero || format != 32 || count == 0)
                return 0;

            var raw = Marshal.ReadIntPtr(data).ToInt64();
            return raw is > 0 and <= int.MaxValue ? (int)raw : 0;
        }
        finally
        {
            if (data != IntPtr.Zero)
                X11Native.XFree(data);
        }
    }

    private static bool TryGetGeometry(IntPtr display, IntPtr window)
    {
        return X11Native.XGetGeometry(
            display,
            window,
            out _,
            out _,
            out _,
            out var width,
            out var height,
            out _,
            out _) != 0
            && width > 8
            && height > 8;
    }

    private static string ReadProcessName(int pid)
    {
        if (pid <= 0)
            return "x11";

        try
        {
            var comm = $"/proc/{pid}/comm";
            if (File.Exists(comm))
                return File.ReadAllText(comm).Trim();
        }
        catch
        {
        }

        return $"pid-{pid}";
    }

    private static bool TryGetProperty(
        IntPtr display,
        IntPtr window,
        IntPtr atom,
        out IntPtr data,
        out int format,
        out nuint count)
    {
        var result = X11Native.XGetWindowProperty(
            display,
            window,
            atom,
            IntPtr.Zero,
            new IntPtr(1024 * 1024),
            false,
            IntPtr.Zero,
            out _,
            out format,
            out count,
            out _,
            out data);

        return result == Success;
    }
}
