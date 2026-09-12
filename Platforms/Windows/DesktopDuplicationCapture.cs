using System.Drawing;
using System.Drawing.Imaging;
using SharpDX;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using DrawingPixelFormat = System.Drawing.Imaging.PixelFormat;
using DxgiResource = SharpDX.DXGI.Resource;
using D3D11Device = SharpDX.Direct3D11.Device;

namespace WindowSnapper.Platforms.Windows;

internal static class DesktopDuplicationCapture
{
    private const int FrameWaitMs = 250;
    private const int FrameAttempts = 4;

    public static BitmapCapture CaptureWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd))
            return new BitmapCapture(null, "target window is unavailable");
        if (NativeMethods.IsIconic(hwnd))
            return new BitmapCapture(null, "target is minimized");
        if (!NativeMethods.GetWindowRect(hwnd, out var windowRect))
            return new BitmapCapture(null, "could not read target bounds");

        var monitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
        if (monitor == IntPtr.Zero)
            return new BitmapCapture(null, "could not find the target monitor");

        var monitorInfo = new NativeMethods.MONITORINFO
        {
            cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFO>()
        };
        if (!NativeMethods.GetMonitorInfo(monitor, ref monitorInfo))
            return new BitmapCapture(null, "could not read target monitor bounds");

        try
        {
            using var factory = new Factory1();
            var adapters = factory.Adapters1;
            try
            {
                foreach (var adapter in adapters)
                {
                    var outputs = adapter.Outputs;
                    foreach (var output in outputs)
                    {
                        using (output)
                        {
                            var description = output.Description;
                            if (!SameBounds(description.DesktopBounds, monitorInfo.rcMonitor))
                                continue;

                            return CaptureOutput(adapter, output, windowRect, monitorInfo.rcMonitor);
                        }
                    }
                }
            }
            finally
            {
                foreach (var adapter in adapters)
                    adapter.Dispose();
            }

            return new BitmapCapture(null, "DXGI could not match the target monitor to an output");
        }
        catch (SharpDXException ex)
        {
            return new BitmapCapture(null, $"{ex.ResultCode}: {ex.Message}");
        }
        catch (Exception ex)
        {
            return new BitmapCapture(null, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static BitmapCapture CaptureOutput(
        Adapter1 adapter,
        Output output,
        NativeMethods.RECT windowRect,
        NativeMethods.RECT monitorRect)
    {
        using var output1 = output.QueryInterface<Output1>();
        using var device = new D3D11Device(adapter);
        using var duplication = output1.DuplicateOutput(device);

        var outputDescription = output.Description;
        var outputBounds = outputDescription.DesktopBounds;
        if (outputBounds.Right <= outputBounds.Left || outputBounds.Bottom <= outputBounds.Top)
            return new BitmapCapture(null, "DXGI output has no size");

        DxgiResource? desktopResource = null;
        var frameHeld = false;

        try
        {
            for (var attempt = 0; attempt < FrameAttempts; attempt++)
            {
                var frameResult = duplication.TryAcquireNextFrame(FrameWaitMs, out _, out desktopResource);
                if (frameResult.Success)
                {
                    frameHeld = true;
                    break;
                }

                desktopResource?.Dispose();
                desktopResource = null;

                if (frameResult.Code == SharpDX.DXGI.ResultCode.WaitTimeout.Result.Code)
                {
                    // Nothing moved. Apparently the desktop has discovered inner peace; ask again.
                    continue;
                }

                return new BitmapCapture(null, $"DXGI frame acquisition failed ({frameResult.Code})");
            }

            if (!frameHeld || desktopResource is null)
                return new BitmapCapture(null, "timed out waiting for a duplicated desktop frame");

            using var source = desktopResource.QueryInterface<Texture2D>();
            var sourceDescription = source.Description;
            var textureDescription = new Texture2DDescription
            {
                CpuAccessFlags = CpuAccessFlags.Read,
                BindFlags = BindFlags.None,
                Format = Format.B8G8R8A8_UNorm,
                Width = sourceDescription.Width,
                Height = sourceDescription.Height,
                OptionFlags = ResourceOptionFlags.None,
                MipLevels = 1,
                ArraySize = 1,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Staging
            };

            using var staging = new Texture2D(device, textureDescription);
            device.ImmediateContext.CopyResource(source, staging);

            var mapped = device.ImmediateContext.MapSubresource(staging, 0, MapMode.Read, SharpDX.Direct3D11.MapFlags.None);
            try
            {
                using var rawOutput = CopyMappedFrame(mapped, sourceDescription.Width, sourceDescription.Height);
                using var orientedOutput = OrientOutput(rawOutput, outputDescription.Rotation);
                return new BitmapCapture(CropTarget(orientedOutput, windowRect, monitorRect), string.Empty);
            }
            finally
            {
                device.ImmediateContext.UnmapSubresource(staging, 0);
            }
        }
        catch (SharpDXException ex)
        {
            return new BitmapCapture(null, $"{ex.ResultCode}: {ex.Message}");
        }
        finally
        {
            desktopResource?.Dispose();
            if (frameHeld)
            {
                try
                {
                    duplication.ReleaseFrame();
                }
                catch
                {
                    // The display can mode-switch under us. ReleaseFrame failing during teardown is just DXGI saying goodbye loudly.
                }
            }
        }
    }

    private static Bitmap CopyMappedFrame(DataBox mapped, int width, int height)
    {
        var bitmap = new Bitmap(width, height, DrawingPixelFormat.Format32bppArgb);
        var area = new System.Drawing.Rectangle(0, 0, width, height);
        var data = bitmap.LockBits(area, ImageLockMode.WriteOnly, bitmap.PixelFormat);

        try
        {
            var source = mapped.DataPointer;
            var destination = data.Scan0;
            var rowBytes = width * 4;

            for (var y = 0; y < height; y++)
            {
                Utilities.CopyMemory(destination, source, rowBytes);
                source = IntPtr.Add(source, mapped.RowPitch);
                destination = IntPtr.Add(destination, data.Stride);
            }
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return bitmap;
    }


    private static Bitmap OrientOutput(Bitmap source, DisplayModeRotation rotation)
    {
        var result = new Bitmap(source);
        switch (rotation)
        {
            case DisplayModeRotation.Rotate90:
                result.RotateFlip(RotateFlipType.Rotate90FlipNone);
                break;
            case DisplayModeRotation.Rotate180:
                result.RotateFlip(RotateFlipType.Rotate180FlipNone);
                break;
            case DisplayModeRotation.Rotate270:
                result.RotateFlip(RotateFlipType.Rotate270FlipNone);
                break;
        }

        return result;
    }

    private static Bitmap CropTarget(Bitmap output, NativeMethods.RECT window, NativeMethods.RECT monitor)
    {
        const int edgeSlop = 4;
        var coversMonitor = window.Left <= monitor.Left + edgeSlop
                            && window.Top <= monitor.Top + edgeSlop
                            && window.Right >= monitor.Right - edgeSlop
                            && window.Bottom >= monitor.Bottom - edgeSlop;

        if (coversMonitor)
            return new Bitmap(output);

        var left = Math.Max(window.Left, monitor.Left);
        var top = Math.Max(window.Top, monitor.Top);
        var right = Math.Min(window.Right, monitor.Right);
        var bottom = Math.Min(window.Bottom, monitor.Bottom);
        if (right <= left || bottom <= top)
            return new Bitmap(output);

        var source = new System.Drawing.Rectangle(
            left - monitor.Left,
            top - monitor.Top,
            right - left,
            bottom - top);

        // Clone instead of drawing through GDI so the fallback stays as passive as the capture itself.
        return output.Clone(source, output.PixelFormat);
    }

    private static bool SameBounds(SharpDX.Mathematics.Interop.RawRectangle output, NativeMethods.RECT monitor) =>
        output.Left == monitor.Left
        && output.Top == monitor.Top
        && output.Right == monitor.Right
        && output.Bottom == monitor.Bottom;
}
