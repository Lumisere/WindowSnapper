using WindowSnapper.Models;
#if WINDOWS
using WindowSnapper.Platforms.Windows;
#else
using WindowSnapper.Platforms.Linux;
#endif

namespace WindowSnapper.Services;

public static class WindowFinder
{
    public static IReadOnlyList<WindowInfo> GetOpenWindows()
    {
#if WINDOWS
        return WindowsWindowFinder.GetOpenWindows();
#else
        return PlatformInfo.HasX11
            ? LinuxX11WindowFinder.GetOpenWindows()
            : Array.Empty<WindowInfo>();
#endif
    }

    public static WindowInfo? Resolve(TargetMode mode, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var windows = GetOpenWindows();
        return mode switch
        {
            TargetMode.ProcessExe => windows.FirstOrDefault(w =>
                string.Equals(NormalizeExe(w.ProcessName), NormalizeExe(value), StringComparison.OrdinalIgnoreCase)),
            TargetMode.WindowTitle => windows.FirstOrDefault(w =>
                w.Title.Contains(value.Trim(), StringComparison.OrdinalIgnoreCase)),
            TargetMode.WindowHandle => ResolveHandle(windows, value),
            _ => null
        };
    }


    public static WindowInfo? ResolveNativeId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var wanted = NormalizeNativeId(value);
        return GetOpenWindows().FirstOrDefault(w =>
            w.HasNativeId && string.Equals(NormalizeNativeId(w.NativeId), wanted, StringComparison.OrdinalIgnoreCase));
    }

    public static bool TryParseHandle(string value, out nint handle)
    {
        var text = value.Trim();
        var hex = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
        if (hex)
            text = text[2..];

        var style = hex ? System.Globalization.NumberStyles.HexNumber : System.Globalization.NumberStyles.Integer;
        if (long.TryParse(text, style, System.Globalization.CultureInfo.InvariantCulture, out var parsed) && parsed != 0)
        {
            handle = (nint)parsed;
            return true;
        }

        handle = 0;
        return false;
    }

    private static WindowInfo? ResolveHandle(IReadOnlyList<WindowInfo> windows, string value)
    {
        var text = value.Trim();
        var native = windows.FirstOrDefault(w =>
            w.HasNativeId && string.Equals(NormalizeNativeId(w.NativeId), NormalizeNativeId(text), StringComparison.OrdinalIgnoreCase));
        if (native is not null)
            return native;

        if (!TryParseHandle(text, out var handle))
            return null;

        return windows.FirstOrDefault(w => w.Handle == handle) ?? new WindowInfo(handle, string.Empty, string.Empty, 0);
    }



    private static string NormalizeNativeId(string value) => value.Trim().Trim('{', '}');

    private static string NormalizeExe(string value)
    {
        var name = Path.GetFileName(value.Trim());
        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
    }
}
