#if WINDOWS
using WindowSnapper.Platforms.Windows;
#else
using WindowSnapper.Platforms.Linux;
#endif

namespace WindowSnapper.Services;

public static class NotificationSoundService
{
    public readonly record struct PlaybackResult(
        bool Success,
        string DisplayName,
        bool UsedFallback,
        string? Error = null);

    public static string DefaultSoundPath => Path.Combine(AppContext.BaseDirectory, "notif.mp3");

    public static PlaybackResult Play(string? customPath, double volume = 1.0)
    {
        var custom = !string.IsNullOrWhiteSpace(customPath);
        var requested = custom ? customPath! : DefaultSoundPath;
        volume = Math.Clamp(double.IsFinite(volume) ? volume : 1.0, 0.0, 1.0);

        if (TryPlay(requested, volume, out var error))
            return new PlaybackResult(true, DisplayName(customPath), false);

        if (custom && TryPlay(DefaultSoundPath, volume, out _))
        {
            return new PlaybackResult(
                true,
                "notif.mp3 (default)",
                true,
                error ?? "The selected sound could not be played.");
        }

        return new PlaybackResult(
            false,
            DisplayName(customPath),
            custom,
            error ?? "The notification sound could not be played.");
    }

    public static void Stop()
    {
#if WINDOWS
        WindowsAudioPlayer.Stop();
#else
        LinuxAudioPlayer.Stop();
#endif
    }

    public static string DisplayName(string? customPath) => string.IsNullOrWhiteSpace(customPath)
        ? "notif.mp3 (default)"
        : Path.GetFileName(customPath);

    private static bool TryPlay(string path, double volume, out string? error)
    {
        if (!File.Exists(path))
        {
            error = $"Sound file not found: {Path.GetFileName(path)}";
            return false;
        }

#if WINDOWS
        return WindowsAudioPlayer.TryPlay(path, volume, out error);
#else
        return LinuxAudioPlayer.TryPlay(path, volume, out error);
#endif
    }
}
