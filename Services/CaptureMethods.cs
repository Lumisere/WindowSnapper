using System.Drawing;
using System.Drawing.Imaging;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using D3D11Device = SharpDX.Direct3D11.Device;
using DrawingPixelFormat = System.Drawing.Imaging.PixelFormat;

namespace WindowSnapper.Services;

internal static class CaptureMethods
{
    public static async Task<BitmapCapture> WindowsGraphicsAsync(IntPtr hwnd)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 18362))
            return new BitmapCapture(null, "requires Windows 10 version 1903 or newer");

        if (!GraphicsCaptureSession.IsSupported())
            return new BitmapCapture(null, "not supported on this system");

        if (NativeMethods.IsIconic(hwnd))
            return new BitmapCapture(null, "the target is minimized");

        try
        {
            using var device = new D3D11Device(
                SharpDX.Direct3D.DriverType.Hardware,
                DeviceCreationFlags.BgraSupport);

            if (device.NativePointer == IntPtr.Zero || device.IsDisposed)
                return new BitmapCapture(null, "Direct3D device creation failed");

            var winRtDevice = Direct3D11Helper.CreateWinRtDevice(device);
            var item = CaptureInterop.CreateForWindow(hwnd);
            var size = item.Size;
            if (size.Width <= 0 || size.Height <= 0)
                return new BitmapCapture(null, "target has no capturable size");

            using var framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                winRtDevice,
                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                2,
                size);
            using var session = framePool.CreateCaptureSession(item);

            var firstFrame = new TaskCompletionSource<BitmapCapture>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            void OnFrameArrived(Direct3D11CaptureFramePool pool, object args)
            {
                try
                {
                    using var frame = pool.TryGetNextFrame();
                    if (frame is null)
                        return;

                    using var texture = Direct3D11Helper.GetTexture(frame.Surface);
                    var contentSize = frame.ContentSize;
                    var width = Math.Min(contentSize.Width, texture.Description.Width);
                    var height = Math.Min(contentSize.Height, texture.Description.Height);

                    if (width <= 0 || height <= 0)
                    {
                        firstFrame.TrySetResult(new BitmapCapture(null, "captured frame has no size"));
                        return;
                    }

                    firstFrame.TrySetResult(new BitmapCapture(CopyTexture(device, texture, width, height), string.Empty));
                }
                catch (Exception ex)
                {
                    firstFrame.TrySetResult(new BitmapCapture(null, $"{ex.GetType().Name}: {ex.Message}"));
                }
            }

            framePool.FrameArrived += OnFrameArrived;
            try
            {
                session.StartCapture();
                var completed = await Task.WhenAny(firstFrame.Task, Task.Delay(TimeSpan.FromSeconds(2)));
                return completed == firstFrame.Task
                    ? await firstFrame.Task
                    : new BitmapCapture(null, "timed out waiting for a frame");
            }
            finally
            {
                framePool.FrameArrived -= OnFrameArrived;
            }
        }
        catch (Exception ex)
        {
            return new BitmapCapture(null, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    public static BitmapCapture Dxgi(IntPtr hwnd)
    {
        if (NativeMethods.IsIconic(hwnd))
            return new BitmapCapture(null, "the target is minimized");

        var windowRect = GetWindowRect(hwnd);
        if (windowRect.Width <= 0 || windowRect.Height <= 0)
            return new BitmapCapture(null, "target has no visible bounds");

        try
        {
            using var factory = new Factory1();
            if (!FindOutput(factory, windowRect, out var adapterIndex, out var outputIndex, out var outputBounds))
                return new BitmapCapture(null, "window is not on an active desktop output");

            using var adapter = factory.GetAdapter1(adapterIndex);
            using var output = adapter.GetOutput(outputIndex);
            using var output1 = output.QueryInterface<Output1>();
            using var device = new D3D11Device(adapter, DeviceCreationFlags.BgraSupport);

            if (device.NativePointer == IntPtr.Zero || device.IsDisposed)
                return new BitmapCapture(null, "DXGI could not create a Direct3D device");

            var context = device.ImmediateContext;
            if (context is null || context.NativePointer == IntPtr.Zero)
                return new BitmapCapture(null, "DXGI Direct3D device has no immediate context");

            using var duplication = output1.DuplicateOutput(device);
            if (duplication.NativePointer == IntPtr.Zero)
                return new BitmapCapture(null, "DXGI output duplication failed");

            return ReadDuplicatedFrame(duplication, device, context, windowRect, outputBounds);
        }
        catch (Exception ex)
        {
            return new BitmapCapture(null, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    public static Bitmap? PrintWindow(IntPtr hwnd)
    {
        var rect = GetWindowRect(hwnd);
        if (rect.Width <= 0 || rect.Height <= 0)
            return null;

        var bitmap = new Bitmap(rect.Width, rect.Height, DrawingPixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        var hdc = graphics.GetHdc();

        bool captured;
        try
        {
            captured = NativeMethods.PrintWindow(hwnd, hdc, NativeMethods.PW_RENDERFULLCONTENT);
        }
        finally
        {
            graphics.ReleaseHdc(hdc);
        }

        if (captured)
            return bitmap;

        bitmap.Dispose();
        return null;
    }

    public static Bitmap? ScreenCopy(IntPtr hwnd)
    {
        if (NativeMethods.IsIconic(hwnd))
            return null;

        var rect = GetWindowRect(hwnd);
        if (rect.Width <= 0 || rect.Height <= 0)
            return null;

        var bitmap = new Bitmap(rect.Width, rect.Height, DrawingPixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(
            rect.Left,
            rect.Top,
            0,
            0,
            new Size(rect.Width, rect.Height),
            CopyPixelOperation.SourceCopy);
        return bitmap;
    }


    //much similar to my brain after writing this it checks if the bitmap is mostly black, which can be used to determine if the capture was successful or not
    public static bool LooksBlank(Bitmap bitmap)
    {
        var samples = 0;
        var darkSamples = 0;
        var stepX = Math.Max(1, bitmap.Width / 10);
        var stepY = Math.Max(1, bitmap.Height / 10);

        for (var y = stepY / 2; y < bitmap.Height; y += stepY)
        {
            for (var x = stepX / 2; x < bitmap.Width; x += stepX)
            {
                var pixel = bitmap.GetPixel(x, y);
                samples++;
                if (pixel.R < 8 && pixel.G < 8 && pixel.B < 8)
                    darkSamples++;
            }
        }

        return samples > 0 && darkSamples >= samples * 0.96;
    }
    // This method finds the output (monitor) that has the largest overlap with the specified window rectangle.
    private static bool FindOutput(
        Factory1 factory,
        NativeMethods.RECT windowRect,
        out int adapterIndex,
        out int outputIndex,
        out SharpDX.Mathematics.Interop.RawRectangle bounds)
    {
        adapterIndex = -1;
        outputIndex = -1;
        bounds = default;
        long largestOverlap = 0;

        var adapters = factory.Adapters1;
        try
        {
            for (var adapterNumber = 0; adapterNumber < adapters.Length; adapterNumber++)
            {
                var outputs = adapters[adapterNumber].Outputs;
                try
                {
                    for (var outputNumber = 0; outputNumber < outputs.Length; outputNumber++)
                    {
                        var desktopBounds = outputs[outputNumber].Description.DesktopBounds;
                        var overlap = IntersectionArea(windowRect, desktopBounds);
                        if (overlap <= largestOverlap)
                            continue;

                        largestOverlap = overlap;
                        adapterIndex = adapterNumber;
                        outputIndex = outputNumber;
                        bounds = desktopBounds;
                    }
                }
                finally
                {
                    foreach (var output in outputs)
                        output.Dispose();
                }
            }
        }
        finally
        {
            foreach (var adapter in adapters)
                adapter.Dispose();
        }

        return adapterIndex >= 0 && outputIndex >= 0 && largestOverlap > 0;
    }
    // This method reads a duplicated frame from the specified output duplication and returns it as a BitmapCapture.
    private static BitmapCapture ReadDuplicatedFrame(
        OutputDuplication duplication,
        D3D11Device device,
        DeviceContext context,
        NativeMethods.RECT windowRect,
        SharpDX.Mathematics.Interop.RawRectangle outputBounds)
    {
        SharpDX.DXGI.Resource? desktopResource = null;
        var frameAcquired = false;

        try
        {
            for (var attempt = 0; attempt < 2 && !frameAcquired; attempt++)
            {
                var result = duplication.TryAcquireNextFrame(750, out _, out var frameResource);
                if (result.Success)
                {
                    desktopResource = frameResource;
                    frameAcquired = desktopResource is not null;
                    continue;
                }

                frameResource?.Dispose();
                if (result.Code != SharpDX.DXGI.ResultCode.WaitTimeout.Result.Code)
                    return new BitmapCapture(null, $"DXGI frame acquisition failed (0x{result.Code:X8})");
            }

            if (!frameAcquired || desktopResource is null)
                return new BitmapCapture(null, "timed out waiting for a desktop frame");

            using var desktopTexture = desktopResource.QueryInterface<Texture2D>();
            var source = desktopTexture.Description;

            if (source.Width <= 0 || source.Height <= 0)
                return new BitmapCapture(null, "DXGI returned a zero-sized desktop texture");
            if (source.Format != Format.B8G8R8A8_UNorm)
                return new BitmapCapture(null, $"DXGI returned unsupported desktop format {source.Format}");

            using var staging = CreateStagingTexture(device, source);
            context.CopyResource(desktopTexture, staging);

            var left = Math.Max(windowRect.Left, outputBounds.Left);
            var top = Math.Max(windowRect.Top, outputBounds.Top);
            var right = Math.Min(windowRect.Right, outputBounds.Right);
            var bottom = Math.Min(windowRect.Bottom, outputBounds.Bottom);
            var width = right - left;
            var height = bottom - top;

            if (width <= 0 || height <= 0)
                return new BitmapCapture(null, "window is outside the selected desktop output");

            var mapped = context.MapSubresource(staging, 0, MapMode.Read, SharpDX.Direct3D11.MapFlags.None);
            if (mapped.DataPointer == IntPtr.Zero)
                return new BitmapCapture(null, "DXGI mapped the desktop texture with a null data pointer");

            try
            {
                var sourceX = left - outputBounds.Left;
                var sourceY = top - outputBounds.Top;
                var bitmap = CopyMappedRegion(mapped, sourceX, sourceY, width, height);
                return new BitmapCapture(bitmap, string.Empty);
            }
            finally
            {
                Direct3D11Native.Unmap(context, staging, 0);
            }
        }
        finally
        {
            desktopResource?.Dispose();
            if (frameAcquired)
            {
                try
                {
                    duplication.ReleaseFrame();
                }
                catch
                {
                }
            }
        }
    }
    // This method creates a staging texture with the same dimensions and format as the source texture, which can be used for CPU read access or smth x3
    private static Texture2D CreateStagingTexture(D3D11Device device, Texture2DDescription source)
    {
        var description = new Texture2DDescription
        {
            Width = source.Width,
            Height = source.Height,
            MipLevels = 1,
            ArraySize = 1,
            Format = source.Format,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Staging,
            BindFlags = BindFlags.None,
            CpuAccessFlags = CpuAccessFlags.Read,
            OptionFlags = ResourceOptionFlags.None
        };

        return new Texture2D(device, description);
    }

    private static Bitmap CopyTexture(D3D11Device device, Texture2D source, int width, int height)
    {
        if (device.NativePointer == IntPtr.Zero || device.IsDisposed)
            throw new InvalidOperationException("Direct3D device is no longer valid.");
        if (source.NativePointer == IntPtr.Zero || source.IsDisposed)
            throw new InvalidOperationException("Capture frame texture is no longer valid.");

        var context = device.ImmediateContext;
        if (context is null || context.NativePointer == IntPtr.Zero)
            throw new InvalidOperationException("Direct3D device has no immediate context.");

        using var staging = CreateStagingTexture(device, source.Description);
        context.CopyResource(source, staging);

        var mapped = context.MapSubresource(staging, 0, MapMode.Read, SharpDX.Direct3D11.MapFlags.None);
        if (mapped.DataPointer == IntPtr.Zero)
            throw new InvalidOperationException("Capture frame mapped to a null data pointer.");

        try
        {
            return CopyMappedRegion(mapped, 0, 0, width, height);
        }
        finally
        {
            Direct3D11Native.Unmap(context, staging, 0);
        }
    }

    private static Bitmap CopyMappedRegion(SharpDX.DataBox mapped, int sourceX, int sourceY, int width, int height)
    {
        var bitmap = new Bitmap(width, height, DrawingPixelFormat.Format32bppArgb);
        var bits = bitmap.LockBits(
            new Rectangle(0, 0, width, height),
            ImageLockMode.WriteOnly,
            DrawingPixelFormat.Format32bppArgb);

        try
        {
            var source = IntPtr.Add(mapped.DataPointer, sourceY * mapped.RowPitch + sourceX * 4);
            var destination = bits.Scan0;

            for (var y = 0; y < height; y++)
            {
                SharpDX.Utilities.CopyMemory(destination, source, width * 4);
                source = IntPtr.Add(source, mapped.RowPitch);
                destination = IntPtr.Add(destination, bits.Stride);
            }
        }
        finally
        {
            bitmap.UnlockBits(bits);
        }

        return bitmap;
    }

    private static NativeMethods.RECT GetWindowRect(IntPtr hwnd)
    {
        var result = NativeMethods.DwmGetWindowAttribute(
            hwnd,
            NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS,
            out var frame,
            System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.RECT>());

        if (result == 0 && frame.Width > 0 && frame.Height > 0)
            return frame;

        NativeMethods.GetWindowRect(hwnd, out var rect);
        return rect;
    }

    private static long IntersectionArea(
        NativeMethods.RECT window,
        SharpDX.Mathematics.Interop.RawRectangle output)
    {
        var left = Math.Max(window.Left, output.Left);
        var top = Math.Max(window.Top, output.Top);
        var right = Math.Min(window.Right, output.Right);
        var bottom = Math.Min(window.Bottom, output.Bottom);

        return right <= left || bottom <= top
            ? 0
            : (long)(right - left) * (bottom - top);
    }
}
