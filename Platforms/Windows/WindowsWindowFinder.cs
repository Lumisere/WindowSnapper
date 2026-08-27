using System.Diagnostics;
using System.Text;
using WindowSnapper.Models;

namespace WindowSnapper.Platforms.Windows;

internal static class WindowsWindowFinder
{
    public static IReadOnlyList<WindowInfo> GetOpenWindows()
    {
        var windows = new List<WindowInfo>();

        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (!NativeMethods.IsWindowVisible(hwnd))
                return true;

            var length = NativeMethods.GetWindowTextLength(hwnd);
            if (length <= 0)
                return true;

            var text = new StringBuilder(length + 1);
            NativeMethods.GetWindowText(hwnd, text, text.Capacity);
            var title = text.ToString().Trim();
            if (title.Length == 0)
                return true;

            NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
            var processName = string.Empty;
            try
            {
                using var process = Process.GetProcessById((int)pid);
                processName = process.ProcessName;
            }
            catch
            {
                // The process may close between enumeration and lookup.
            }

            windows.Add(new WindowInfo(hwnd, title, processName, (int)pid));
            return true;
        }, IntPtr.Zero);

        return windows
            .OrderBy(w => w.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(w => w.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
