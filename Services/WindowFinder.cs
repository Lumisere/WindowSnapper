using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using WindowSnapper.Models;

namespace WindowSnapper.Services;

public sealed record WindowInfo(IntPtr Handle, string Title, string ProcessName, int ProcessId)
{
    public string Display => $"{Title}  —  {ProcessName}.exe  [0x{Handle.ToInt64():X}]";
}

public static class WindowFinder
{
    public static List<WindowInfo> GetOpenWindows()
    {
        var windows = new List<WindowInfo>();

        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (!NativeMethods.IsWindowVisible(hwnd))
                return true;

            var length = NativeMethods.GetWindowTextLength(hwnd);
            if (length == 0)
                return true;

            var buffer = new StringBuilder(length + 1);
            NativeMethods.GetWindowText(hwnd, buffer, buffer.Capacity);
            var title = buffer.ToString().Trim();
            if (title.Length == 0)
                return true;

            NativeMethods.GetWindowThreadProcessId(hwnd, out var processId);
            try
            {
                using var process = Process.GetProcessById((int)processId);
                windows.Add(new WindowInfo(hwnd, title, process.ProcessName, (int)processId));
            }
            catch (Exception)
            {
                // Windows can disappear between EnumWindows and GetProcessById.
            }

            return true;
        }, IntPtr.Zero);

        return windows
            .OrderBy(window => window.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(window => window.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static IntPtr Resolve(TargetMode mode, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return IntPtr.Zero;

        return mode switch
        {
            TargetMode.ProcessExe => ResolveByProcess(value),
            TargetMode.WindowTitle => ResolveByTitle(value),
            TargetMode.WindowHandle => ResolveByHandle(value),
            _ => IntPtr.Zero
        };
    }

    private static IntPtr ResolveByProcess(string value)
    {
        var processName = Path.GetFileNameWithoutExtension(value.Trim());
        return FindLargestWindow(window =>
            string.Equals(window.ProcessName, processName, StringComparison.OrdinalIgnoreCase));
    }

    private static IntPtr ResolveByTitle(string value)
    {
        var text = value.Trim();
        return FindLargestWindow(window => window.Title.Contains(text, StringComparison.OrdinalIgnoreCase));
    }

    private static IntPtr FindLargestWindow(Func<WindowInfo, bool> predicate)
    {
        var match = GetOpenWindows()
            .Where(predicate)
            .OrderByDescending(window => WindowArea(window.Handle))
            .FirstOrDefault();

        return match?.Handle ?? IntPtr.Zero;
    }

    private static IntPtr ResolveByHandle(string value)
    {
        var text = value.Trim();
        long handleValue;

        var parsed = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? long.TryParse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out handleValue)
            : long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out handleValue);

        if (!parsed)
            return IntPtr.Zero;

        var hwnd = new IntPtr(handleValue);
        return NativeMethods.IsWindow(hwnd) ? hwnd : IntPtr.Zero;
    }

    private static long WindowArea(IntPtr hwnd)
    {
        if (!NativeMethods.GetWindowRect(hwnd, out var rect))
            return 0;

        return Math.Max(0, (long)rect.Width * rect.Height);
    }
}
