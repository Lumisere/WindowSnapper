namespace WindowSnapper.Services;

public static class PlatformInfo
{
    public static bool IsWindows => OperatingSystem.IsWindows();
    public static bool IsLinux => OperatingSystem.IsLinux();

    public static bool IsWayland
    {
        get
        {
            if (!IsLinux)
                return false;

            var session = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE");
            return string.Equals(session, "wayland", StringComparison.OrdinalIgnoreCase)
                || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));
        }
    }

    public static bool HasX11 => IsLinux && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DISPLAY"));

    public static string DesktopName => IsLinux
        ? Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP")
            ?? Environment.GetEnvironmentVariable("XDG_SESSION_DESKTOP")
            ?? string.Empty
        : string.Empty;

    public static bool IsGnome => DesktopContains("GNOME") || DesktopContains("ubuntu");
    public static bool IsKde => DesktopContains("KDE") || DesktopContains("Plasma");
    public static bool IsXfce => DesktopContains("XFCE");

    public static bool IsSway => IsLinux
        && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SWAYSOCK"));

    public static bool IsHyprland => IsLinux
        && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HYPRLAND_INSTANCE_SIGNATURE"));

    public static bool IsKnownWlroots => IsSway
        || IsHyprland
        || DesktopContains("river")
        || DesktopContains("wayfire")
        || DesktopContains("labwc");

    public static bool CommandExists(string command)
    {
        if (!IsLinux || string.IsNullOrWhiteSpace(command))
            return false;

        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        return path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(folder => Path.Combine(folder, command))
            .Any(File.Exists);
    }

    private static bool DesktopContains(string value) =>
        DesktopName.Contains(value, StringComparison.OrdinalIgnoreCase);

}
