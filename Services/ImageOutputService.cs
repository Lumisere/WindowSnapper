using ImageMagick;
using ImageMagick.Drawing;
using WindowSnapper.Models;

namespace WindowSnapper.Services;

public static class ImageOutputService
{
    public static Task<CaptureResult> SaveAsync(byte[] sourceImage, CaptureSettings settings, string backendName) =>
        Task.Run(() => Save(sourceImage, settings, backendName));

    public static CaptureResult Save(byte[] sourceImage, CaptureSettings settings, string backendName)
    {
        if (sourceImage.Length == 0)
            return new CaptureResult(false, "Capture engine returned no image");

        try
        {
            var now = DateTimeOffset.Now;
            using var image = new MagickImage(sourceImage);
            image.AutoOrient();
            image.Strip();
            image.ColorSpace = ColorSpace.sRGB;
            image.Depth = 8;

            // 1080p is home base. Bigger screenshots get a bigger proof so Discord has fewer chances to turn it into soup.
            var resolutionScale = Math.Sqrt(
                Math.Max(1.0, (double)image.Width * image.Height) / (1920.0 * 1080.0));
            var requestedProofScale = Math.Clamp(settings.ToastScale, 0.8, 2.0) * resolutionScale;
            var proofScale = Math.Clamp(
                Math.Round((Math.Clamp(requestedProofScale, 0.8, 2.0) - 0.8) / 0.05) * 0.05 + 0.8,
                0.8,
                2.0);
            var id = settings.CaptureWatermarkEnabled
                ? CaptureVerificationService.CreateCaptureId(now, proofScale)
                : CaptureVerificationService.CreateCaptureId(now);

            var verificationCode = CaptureVerificationService.CreateVerificationCode(settings, id);
            if (settings.CaptureWatermarkEnabled)
            {
                var box = V5.A(image.Width, image.Height, proofScale, carrierRows: 16);
                DrawCaptureIdentityBase(image, id, verificationCode, box);

                // Lossy codecs lie about pixels. Encode/decode once and fingerprint what actually survives.
                string contentProof;
                if (settings.ImageFormat == ImageFormatChoice.Png)
                {
                    contentProof = Z7.D(settings, image, id, box);
                }
                else
                {
                    using var descriptorReference = CreateDescriptorReference(image, settings.ImageFormat);
                    contentProof = Z7.D(settings, image, descriptorReference, id, box);
                }

                Z7.E(image, id, contentProof, box);
            }

            var identity = new CaptureIdentity(now, id, verificationCode);
            ConfigureEncoder(image, settings.ImageFormat);

            var path = OutputPathBuilder.Build(settings, identity.Timestamp);
            image.Write(path);
            return new CaptureResult(true, $"Saved with {backendName}", path, identity.Id, identity.Timestamp);
        }
        catch (Exception ex)
        {
            return new CaptureResult(false, $"Image output error: {ex.Message}");
        }
    }

    private static MagickImage CreateDescriptorReference(MagickImage image, ImageFormatChoice format)
    {
        // Use the codec we’re actually shipping. JPEG pretending to be WebP/AVIF was a very dumb bug.
        using var encoded = new MagickImage(image);
        ConfigureEncoder(encoded, format);
        using var stream = new MemoryStream();
        encoded.Write(stream);
        stream.Position = 0;

        var reference = new MagickImage(stream);
        reference.AutoOrient();
        reference.Strip();
        reference.ColorSpace = ColorSpace.sRGB;
        reference.Depth = 8;
        return reference;
    }

    private static void ConfigureEncoder(MagickImage image, ImageFormatChoice format)
    {
        switch (format)
        {
            case ImageFormatChoice.Jpeg:
                image.Format = MagickFormat.Jpeg;
                image.Quality = 92;
                break;
            case ImageFormatChoice.WebP:
                image.Format = MagickFormat.WebP;
                image.Quality = 90;
                break;
            case ImageFormatChoice.Avif:
                var avifInfo = MagickFormatInfo.Create(MagickFormat.Avif);
                if (avifInfo is null || !avifInfo.SupportsWriting)
                    throw new NotSupportedException("This ImageMagick build does not include AVIF writing support.");

                image.Format = MagickFormat.Avif;
                image.Quality = 86;
                image.Settings.SetDefine(MagickFormat.Heic, "speed", "8");
                image.Settings.SetDefine(MagickFormat.Heic, "chroma", "444");
                break;
            default:
                image.Format = MagickFormat.Png;
                break;
        }
    }

    private static void DrawCaptureIdentityBase(MagickImage image, string id, string verificationCode, V5 g)
    {
        var regularFont = OperatingSystem.IsWindows() ? "Segoe UI" : "DejaVu Sans";
        var titleFont = OperatingSystem.IsWindows() ? "Segoe UI Semibold" : "DejaVu Sans Bold";
        var shell = MagickColor.FromRgba(17, 19, 24, 245);
        var edge = MagickColor.FromRgba(60, 65, 74, 255);
        var accent = MagickColor.FromRgba(212, 55, 67, 255);

        new Drawables()
            .FillColor(shell)
            .StrokeColor(edge)
            .StrokeWidth(g.P4)
            .RoundRectangle(g.P0, g.P1, g.P2, g.P3, g.P5, g.P5)
            .Draw(image);

        var railRight = g.P0 + g.P6 + g.P5;
        new Drawables()
            .FillColor(accent)
            .StrokeColor(MagickColors.Transparent)
            .RoundRectangle(g.P0 + g.P4, g.P1 + g.P4, railRight, g.P3 - g.P4,
                Math.Max(1, g.P5 - g.P4), Math.Max(1, g.P5 - g.P4))
            .FillColor(shell)
            .Rectangle(g.P0 + g.P6, g.P1 + g.P4, railRight + g.P4, g.P3 - g.P4)
            .Draw(image);

        // Human-readable up top, robot nonsense in the padding. Everybody wins.
        var message = $"ID {id}   •   V {verificationCode}";
        new Drawables()
            .FillColor(MagickColors.Transparent)
            .StrokeColor(edge)
            .StrokeWidth(g.P4)
            .RoundRectangle(g.P0, g.P1, g.P2, g.P3, g.P5, g.P5)
            .Font(titleFont)
            .FillColor(MagickColor.FromRgba(244, 245, 247, 255))
            .StrokeColor(MagickColors.Transparent)
            .FontPointSize(g.P9)
            .Text(g.P0 + g.P7, g.P11, "WindowSnapper")
            .Font(regularFont)
            .FillColor(MagickColor.FromRgba(169, 173, 182, 255))
            .FontPointSize(g.P10)
            .Text(g.P0 + g.P7, g.P12, message)
            .Draw(image);
    }

}
