using WindowSnapper.Models;

namespace WindowSnapper.Services;

public static class OutputPathBuilder
{
    private static readonly HashSet<string> WindowsReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    public static string Build(CaptureSettings settings, DateTimeOffset? capturedAt = null)
    {
        var safePrefix = SafeFileName(settings.FileNamePrefix);
        // Prefix folder stays. “Cleaning this up” would just invent a migration bug for no reason.
        var outputFolder = Path.Combine(settings.OutputFolder, safePrefix);
        Directory.CreateDirectory(outputFolder);
        var extension = settings.ImageFormat switch
        {
            ImageFormatChoice.Jpeg => "jpg",
            ImageFormatChoice.WebP => "webp",
            ImageFormatChoice.Avif => "avif",
            _ => "png"
        };

        var timestamp = (capturedAt ?? DateTimeOffset.Now).ToString("yyyy-MM-dd_HH-mm-ss-fff");
        return Path.Combine(outputFolder, $"{safePrefix}_{timestamp}.{extension}");
    }

    public static string SafeFileName(string value)
    {
        var name = string.IsNullOrWhiteSpace(value) ? "capture" : value.Trim();
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray())
            .Trim()
            .TrimEnd('.', ' ');

        if (cleaned.Length == 0 || cleaned is "." or "..")
            return "capture";

        if (OperatingSystem.IsWindows())
        {
            var dot = cleaned.IndexOf('.');
            var stem = dot >= 0 ? cleaned[..dot] : cleaned;
            if (WindowsReservedNames.Contains(stem))
                cleaned = "_" + cleaned;
        }

        return cleaned;
    }
}
