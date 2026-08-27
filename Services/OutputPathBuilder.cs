using WindowSnapper.Models;

namespace WindowSnapper.Services;

public static class OutputPathBuilder
{
    public static string Build(CaptureSettings settings)
    {
        Directory.CreateDirectory(settings.OutputFolder);
        var extension = settings.ImageFormat switch
        {
            ImageFormatChoice.Jpeg => "jpg",
            ImageFormatChoice.WebP => "webp",
            ImageFormatChoice.Avif => "avif",
            _ => "png"
        };

        var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss-fff");
        return Path.Combine(settings.OutputFolder, $"{SafeFileName(settings.FileNamePrefix)}_{timestamp}.{extension}");
    }

    public static string SafeFileName(string value)
    {
        var name = string.IsNullOrWhiteSpace(value) ? "capture" : value.Trim();
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return cleaned.Length == 0 ? "capture" : cleaned;
    }
}
