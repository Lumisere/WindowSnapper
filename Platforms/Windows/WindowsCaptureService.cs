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

                case CaptureBackend.PrintWindow:
                    bitmap = await Task.Run(() => CaptureMethods.PrintWindow(hwnd));
                    backendName = "PrintWindow";
                    break;

                case CaptureBackend.ScreenCopy:
                    bitmap = await Task.Run(() => CaptureMethods.ScreenCopy(
                        hwnd,
                        hideTaskbars: !settings.ExclusiveFullscreenCompatibility));
                    backendName = settings.ExclusiveFullscreenCompatibility
                        ? "Screen Copy (exclusive-safe)"
                        : "Screen Copy";
                    break;

                case CaptureBackend.PortableWindow:
                    if (settings.ExclusiveFullscreenCompatibility)
                        return await CaptureExclusiveSafeAsync(settings, hwnd);
                    return await Task.Run(() => CapturePortableWindow(settings, hwnd));

                default:
                    (bitmap, backendName) = await CaptureAutoAsync(hwnd, settings, errors);
                    break;
            }

            if (bitmap is null)
            {
                var message = errors.Count == 0 ? "Capture engine returned no image" : string.Join("; ", errors);
                return new CaptureResult(false, message);
            }

            var pngBytes = await Task.Run(() => BitmapToPng(bitmap));
            return await ImageOutputService.SaveAsync(pngBytes, settings, backendName);
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
            var data = TakePortableScreenshot(hwnd);
            return ImageOutputService.Save(data, settings, "portable window capture");
        }
        catch (Exception ex)
        {
            return new CaptureResult(false, $"Portable capture: {ex.Message}");
        }
    }

    private static byte[] TakePortableScreenshot(nint hwnd)
    {
        using var taskbars = CaptureMethods.HideTaskbarsForCapture();
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

        return screenshot.TakeScreenshot(options);
    }

    private static BitmapCapture CapturePortableBitmap(nint hwnd)
    {
        try
        {
            var data = TakePortableScreenshot(hwnd);
            if (data.Length == 0)
                return new BitmapCapture(null, "capture engine returned no image");

            using var stream = new MemoryStream(data, writable: false);
            using var decoded = new Bitmap(stream);
            return new BitmapCapture(new Bitmap(decoded), string.Empty);
        }
        catch (Exception ex)
        {
            return new BitmapCapture(null, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static async Task<(Bitmap? Bitmap, string Backend)> CaptureAutoAsync(
        nint hwnd,
        CaptureSettings settings,
        List<string> errors)
    {
        if (settings.ExclusiveFullscreenCompatibility)
        {
            // True FSE can bypass DWM, so go straight to the API Windows documents for fullscreen DirectX duplication.
            var desktop = await Task.Run(() => DesktopDuplicationCapture.CaptureWindow(hwnd));
            if (desktop.Success)
                return (desktop.Bitmap, "DXGI Desktop Duplication (exclusive-safe)");
            errors.Add($"Desktop Duplication: {desktop.Error}");

            var safeWgc = await CaptureMethods.WindowsGraphicsAsync(hwnd, settings.NormalizeHdrCaptures, settings.CaptureCursor);
            if (safeWgc.Success)
                return (safeWgc.Bitmap, "Windows Graphics Capture (exclusive-safe fallback)");
            errors.Add($"WGC: {safeWgc.Error}");
        }
        else
        {
            // Normal Auto stays WGC-first. Fast, clean, and no shell poking when DWM can see the window.
            var wgc = await CaptureMethods.WindowsGraphicsAsync(hwnd, settings.NormalizeHdrCaptures, settings.CaptureCursor);
            if (wgc.Success)
                return (wgc.Bitmap, "Windows Graphics Capture");
            errors.Add($"WGC: {wgc.Error}");
        }

        var printWindow = await Task.Run(() => CaptureMethods.PrintWindow(hwnd));
        if (printWindow is not null && !CaptureMethods.LooksBlank(printWindow))
        {
            return (printWindow, settings.ExclusiveFullscreenCompatibility
                ? "PrintWindow (exclusive-safe fallback)"
                : "PrintWindow fallback");
        }

        printWindow?.Dispose();
        errors.Add("PrintWindow: no usable frame");

        if (!settings.ExclusiveFullscreenCompatibility)
        {
            var portable = await Task.Run(() => CapturePortableBitmap(hwnd));
            if (portable.Success)
                return (portable.Bitmap, "Portable window capture fallback");
            errors.Add($"Portable window capture: {portable.Error}");
        }
        else
        {
            // Shutter is great for normal windows. An exclusive swap chain is not the time to find out what it does to focus.
            errors.Add("Portable window capture: skipped by exclusive fullscreen compatibility");
        }

        var screenCopy = await Task.Run(() => CaptureMethods.ScreenCopy(
            hwnd,
            hideTaskbars: !settings.ExclusiveFullscreenCompatibility));
        if (screenCopy is not null)
        {
            return (screenCopy, settings.ExclusiveFullscreenCompatibility
                ? "Screen Copy (exclusive-safe fallback)"
                : "Screen Copy fallback");
        }

        errors.Add("Screen Copy: window is minimized or unavailable");
        return (null, string.Empty);
    }

    private static async Task<CaptureResult> CaptureExclusiveSafeAsync(CaptureSettings settings, nint hwnd)
    {
        var errors = new List<string>();

        var desktop = await Task.Run(() => DesktopDuplicationCapture.CaptureWindow(hwnd));
        if (desktop.Success)
        {
            using var bitmap = desktop.Bitmap!;
            return await ImageOutputService.SaveAsync(
                BitmapToPng(bitmap),
                settings,
                "DXGI Desktop Duplication (exclusive-safe)");
        }
        errors.Add($"Desktop Duplication: {desktop.Error}");

        var wgc = await CaptureMethods.WindowsGraphicsAsync(hwnd, settings.NormalizeHdrCaptures, settings.CaptureCursor);
        if (wgc.Success)
        {
            using var bitmap = wgc.Bitmap!;
            return await ImageOutputService.SaveAsync(
                BitmapToPng(bitmap),
                settings,
                "Windows Graphics Capture (exclusive-safe fallback)");
        }
        AddError(errors, wgc);

        var printWindow = await Task.Run(() => CaptureMethods.PrintWindow(hwnd));
        if (printWindow is not null && !CaptureMethods.LooksBlank(printWindow))
        {
            using (printWindow)
            {
                return await ImageOutputService.SaveAsync(
                    BitmapToPng(printWindow),
                    settings,
                    "PrintWindow (exclusive-safe fallback)");
            }
        }
        printWindow?.Dispose();
        errors.Add("PrintWindow: no usable frame");

        var screenCopy = await Task.Run(() => CaptureMethods.ScreenCopy(hwnd, hideTaskbars: false));
        if (screenCopy is not null)
        {
            using (screenCopy)
            {
                return await ImageOutputService.SaveAsync(
                    BitmapToPng(screenCopy),
                    settings,
                    "Screen Copy (exclusive-safe fallback)");
            }
        }

        errors.Add("Screen Copy: window is minimized or unavailable");
        return new CaptureResult(false, $"Exclusive-safe capture failed: {string.Join("; ", errors)}");
    }

    private static byte[] BitmapToPng(Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, DrawingImageFormat.Png);
        return stream.ToArray();
    }

    private static void AddError(ICollection<string> errors, BitmapCapture capture)
    {
        if (!capture.Success && !string.IsNullOrWhiteSpace(capture.Error))
            errors.Add(capture.Error);
    }
}
