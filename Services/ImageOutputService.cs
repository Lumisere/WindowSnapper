using ImageMagick;
using WindowSnapper.Models;

namespace WindowSnapper.Services;

public static class ImageOutputService
{
    public static CaptureResult Save(byte[] sourceImage, CaptureSettings settings, string backendName)
    {
        if (sourceImage.Length == 0)
            return new CaptureResult(false, "Capture engine returned no image");

        try
        {
            using var image = new MagickImage(sourceImage);
            image.Strip();
            image.Quality = 92;
            image.Format = settings.ImageFormat switch
            {
                ImageFormatChoice.Jpeg => MagickFormat.Jpeg,
                ImageFormatChoice.WebP => MagickFormat.WebP,
                ImageFormatChoice.Avif => MagickFormat.Avif,
                _ => MagickFormat.Png
            };

            var path = OutputPathBuilder.Build(settings);
            image.Write(path);
            return new CaptureResult(true, $"Saved with {backendName}", path);
        }
        catch (Exception ex)
        {
            return new CaptureResult(false, $"Image output error: {ex.Message}");
        }
    }
}
