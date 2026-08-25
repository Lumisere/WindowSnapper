using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using WindowSnapper.Models;

namespace WindowSnapper.Services;

public sealed record CaptureResult(bool Success, string Message, string? FilePath = null);

internal readonly record struct BitmapCapture(Bitmap? Bitmap, string Error)
{
    public bool Success => Bitmap is not null;
}

public static class CaptureService
{
    public static async Task<CaptureResult> CaptureAsync(CaptureSettings settings)
    {
        var hwnd = WindowFinder.Resolve(settings.TargetMode, settings.TargetValue);
        if (hwnd == IntPtr.Zero)
            return new CaptureResult(false, "Target window not found");

        if (string.IsNullOrWhiteSpace(settings.OutputFolder))
            return new CaptureResult(false, "Choose an output folder");

        Directory.CreateDirectory(settings.OutputFolder);

        Bitmap? bitmap = null;
        var backendName = string.Empty;
        var errors = new List<string>();

        try
        {
            switch (settings.Backend)
            {
                case CaptureBackend.WindowsGraphicsCapture:
                {
                    var capture = await CaptureMethods.WindowsGraphicsAsync(hwnd);
                    bitmap = capture.Bitmap;
                    backendName = "Windows Graphics Capture";
                    AddError(errors, capture);
                    break;
                }

                case CaptureBackend.DxgiDesktopDuplication:
                {
                    var capture = await Task.Run(() => CaptureMethods.Dxgi(hwnd));
                    bitmap = capture.Bitmap;
                    backendName = "DXGI Desktop Duplication";
                    AddError(errors, capture);
                    break;
                }

                case CaptureBackend.PrintWindow:
                    bitmap = await Task.Run(() => CaptureMethods.PrintWindow(hwnd));
                    backendName = "PrintWindow";
                    break;

                case CaptureBackend.ScreenCopy:
                    bitmap = await Task.Run(() => CaptureMethods.ScreenCopy(hwnd));
                    backendName = "Screen Copy";
                    break;

                default:
                    (bitmap, backendName) = await CaptureAutoAsync(hwnd, errors);
                    break;
            }

            if (bitmap is null)
            {
                var message = errors.Count == 0
                    ? "Capture engine returned no image"
                    : string.Join("; ", errors);
                return new CaptureResult(false, message);
            }

            var path = BuildOutputPath(settings);
            SaveBitmap(bitmap, path, settings.ImageFormat);
            return new CaptureResult(true, $"Saved with {backendName}", path);
        }
        catch (Exception ex)
        {
            return new CaptureResult(false, $"{ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            bitmap?.Dispose();
        }
    }
    // this took longer than I expected to implement, but it works well now (I think? Please :sob:) It tries each capture method in order and returns the first successful one, while collecting error messages for any failures.
    private static async Task<(Bitmap? Bitmap, string Backend)> CaptureAutoAsync(IntPtr hwnd, List<string> errors)
    {
        var wgc = await CaptureMethods.WindowsGraphicsAsync(hwnd);
        if (wgc.Success)
            return (wgc.Bitmap, "Windows Graphics Capture");
        errors.Add($"WGC: {wgc.Error}");

        var printWindow = await Task.Run(() => CaptureMethods.PrintWindow(hwnd));
        if (printWindow is not null && !CaptureMethods.LooksBlank(printWindow))
            return (printWindow, "PrintWindow fallback");

        printWindow?.Dispose();
        errors.Add("PrintWindow: no usable frame");

        var dxgi = await Task.Run(() => CaptureMethods.Dxgi(hwnd));
        if (dxgi.Success)
            return (dxgi.Bitmap, "DXGI fallback");
        errors.Add($"DXGI: {dxgi.Error}");

        var screenCopy = await Task.Run(() => CaptureMethods.ScreenCopy(hwnd));
        if (screenCopy is not null)
            return (screenCopy, "Screen Copy fallback");

        errors.Add("Screen Copy: window is minimized or unavailable");
        return (null, string.Empty);
    }

    private static void AddError(List<string> errors, BitmapCapture capture)
    {
        if (!capture.Success && capture.Error.Length != 0)
            errors.Add(capture.Error);
    }

    private static string BuildOutputPath(CaptureSettings settings)
    {
        var extension = settings.ImageFormat == ImageFormatChoice.Jpeg ? "jpg" : "png";
        var prefix = SafeFileName(settings.FileNamePrefix);
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss-fff");
        return Path.Combine(settings.OutputFolder, $"{prefix}_{timestamp}.{extension}");
    }

    private static void SaveBitmap(Bitmap bitmap, string path, ImageFormatChoice format)
    {
        bitmap.Save(path, format == ImageFormatChoice.Jpeg ? ImageFormat.Jpeg : ImageFormat.Png);
    }

    private static string SafeFileName(string value)
    {
        var name = string.IsNullOrWhiteSpace(value) ? "capture" : value.Trim();
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
        return cleaned.Length == 0 ? "capture" : cleaned;
    }
}
