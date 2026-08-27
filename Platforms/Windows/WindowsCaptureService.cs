using System.Drawing;
using System.Drawing.Imaging;
using WindowSnapper.Models;
using WindowSnapper.Services;
using Shutter;
using Shutter.Enums;
using Shutter.Models;
using DrawingImageFormat = System.Drawing.Imaging.ImageFormat;
using ShutterImageFormat = Shutter.Enums.ImageFormat;

namespace WindowSnapper.Platforms.Windows;

internal readonly record struct BitmapCapture(Bitmap? Bitmap, string Error)
{
    public bool Success => Bitmap is not null;
}

internal static class WindowsCaptureService
{
    public static async Task<CaptureResult> CaptureAsync(CaptureSettings settings, nint hwnd)
    {
        Bitmap? bitmap = null;
        var backendName = string.Empty;
        var errors = new List<string>();

        try
        {
            switch (settings.Backend)
            {
                case CaptureBackend.WindowsGraphicsCapture:
                {
                    var capture = await CaptureMethods.WindowsGraphicsAsync(hwnd, settings.NormalizeHdrCaptures, settings.CaptureCursor);
                    bitmap = capture.Bitmap;
                    backendName = "Windows Graphics Capture";
                    AddError(errors, capture);
                    break;
                }

                case CaptureBackend.DxgiDesktopDuplication:
                {
                    var capture = await Task.Run(() => CaptureMethods.Dxgi(hwnd, settings.NormalizeHdrCaptures));
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

                case CaptureBackend.PortableWindow:
                    return await Task.Run(() => CapturePortableWindow(settings, hwnd));

                default:
                    (bitmap, backendName) = await CaptureAutoAsync(hwnd, settings.NormalizeHdrCaptures, settings.CaptureCursor, errors);
                    break;
            }

            if (bitmap is null)
            {
                var message = errors.Count == 0 ? "Capture engine returned no image" : string.Join("; ", errors);
                return new CaptureResult(false, message);
            }

            using var stream = new MemoryStream();
            bitmap.Save(stream, DrawingImageFormat.Png);
            return ImageOutputService.Save(stream.ToArray(), settings, backendName);
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


    private static CaptureResult CapturePortableWindow(CaptureSettings settings, nint hwnd)
    {
        try
        {
            var screenshot = new ShutterService();
            var options = new ScreenshotOptions
            {
                Target = CaptureTarget.Window,
                WindowHandle = hwnd,
                IncludeBorder = true,
                IncludeShadow = false,
                Format = ShutterImageFormat.Png,
                Fallback = FallbackBehavior.ThrowException
            };

            var data = screenshot.TakeScreenshot(options);
            return ImageOutputService.Save(data, settings, "portable window capture");
        }
        catch (Exception ex)
        {
            return new CaptureResult(false, $"Portable capture: {ex.Message}");
        }
    }

    private static async Task<(Bitmap? Bitmap, string Backend)> CaptureAutoAsync(nint hwnd, bool normalizeHdr, bool captureCursor, List<string> errors)
    {
        var wgc = await CaptureMethods.WindowsGraphicsAsync(hwnd, normalizeHdr, captureCursor);
        if (wgc.Success)
            return (wgc.Bitmap, "Windows Graphics Capture");
        errors.Add($"WGC: {wgc.Error}");

        var printWindow = await Task.Run(() => CaptureMethods.PrintWindow(hwnd));
        if (printWindow is not null && !CaptureMethods.LooksBlank(printWindow))
            return (printWindow, "PrintWindow fallback");

        printWindow?.Dispose();
        errors.Add("PrintWindow: no usable frame");

        var dxgi = await Task.Run(() => CaptureMethods.Dxgi(hwnd, normalizeHdr));
        if (dxgi.Success)
            return (dxgi.Bitmap, "DXGI fallback");
        errors.Add($"DXGI: {dxgi.Error}");

        var screenCopy = await Task.Run(() => CaptureMethods.ScreenCopy(hwnd));
        if (screenCopy is not null)
            return (screenCopy, "Screen Copy fallback");

        errors.Add("Screen Copy: window is minimized or unavailable");
        return (null, string.Empty);
    }

    private static void AddError(ICollection<string> errors, BitmapCapture capture)
    {
        if (!capture.Success && !string.IsNullOrWhiteSpace(capture.Error))
            errors.Add(capture.Error);
    }
}
