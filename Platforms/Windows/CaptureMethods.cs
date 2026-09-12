using System.Buffers;
using System.Drawing;
using System.Drawing.Imaging;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using D3D11Device = SharpDX.Direct3D11.Device;
using DrawingPixelFormat = System.Drawing.Imaging.PixelFormat;

namespace WindowSnapper.Platforms.Windows;

internal static class CaptureMethods
{
    public static async Task<BitmapCapture> WindowsGraphicsAsync(IntPtr hwnd, bool normalizeHdr, bool captureCursor)
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

            var frameFormat = normalizeHdr
                ? DirectXPixelFormat.R16G16B16A16Float
                : DirectXPixelFormat.B8G8R8A8UIntNormalized;

            using var framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                winRtDevice,
                frameFormat,
                2,
                size);
            using var session = framePool.CreateCaptureSession(item);
            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
                session.IsCursorCaptureEnabled = captureCursor;

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

                    firstFrame.TrySetResult(new BitmapCapture(CopyTexture(device, texture, width, height, normalizeHdr), string.Empty));
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
                var completed = await Task.WhenAny(firstFrame.Task, Task.Delay(TimeSpan.FromSeconds(1)));
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

    public static Bitmap? ScreenCopy(IntPtr hwnd, bool hideTaskbars = true)
    {
        if (NativeMethods.IsIconic(hwnd))
            return null;

        var rect = GetWindowRect(hwnd);
        if (rect.Width <= 0 || rect.Height <= 0)
            return null;

        var bitmap = new Bitmap(rect.Width, rect.Height, DrawingPixelFormat.Format32bppArgb);
        using var taskbars = hideTaskbars ? HideTaskbarsForCapture() : EmptyGuard.Instance;
        try
        {
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
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    internal static IDisposable HideTaskbarsForCapture()
    {
        var hidden = new List<IntPtr>();

        void HideIfVisible(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero || !NativeMethods.IsWindowVisible(hwnd))
                return;

            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_HIDE);
            hidden.Add(hwnd);
        }

        HideIfVisible(NativeMethods.FindWindow("Shell_TrayWnd", null));

        var after = IntPtr.Zero;
        while (true)
        {
            var secondary = NativeMethods.FindWindowEx(IntPtr.Zero, after, "Shell_SecondaryTrayWnd", null);
            if (secondary == IntPtr.Zero)
                break;

            HideIfVisible(secondary);
            after = secondary;
        }

        if (hidden.Count > 0)
        {
            // Explorer can be one frame behind. Give it a moment so it doesn't photobomb the capture anyway.
            NativeMethods.DwmFlush();
            Thread.Sleep(70);
        }

        return new TaskbarCaptureGuard(hidden);
    }

    private sealed class TaskbarCaptureGuard : IDisposable
    {
        private List<IntPtr>? _taskbars;

        public TaskbarCaptureGuard(List<IntPtr> taskbars) => _taskbars = taskbars;

        public void Dispose()
        {
            var taskbars = Interlocked.Exchange(ref _taskbars, null);
            if (taskbars is null)
                return;

            foreach (var hwnd in taskbars)
            {
                if (NativeMethods.IsWindow(hwnd))
                    NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOW);
            }

            if (taskbars.Count > 0)
                NativeMethods.DwmFlush();
        }
    }


    private sealed class EmptyGuard : IDisposable
    {
        public static readonly EmptyGuard Instance = new();
        public void Dispose() { }
    }

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

    private static Bitmap CopyTexture(D3D11Device device, Texture2D source, int width, int height, bool normalizeHdr)
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
            return CopyMappedFrame(
                mapped,
                width,
                height,
                source.Description.Format,
                normalizeHdr);
        }
        finally
        {
            Direct3D11Native.Unmap(context, staging, 0);
        }
    }

    private static Bitmap CopyMappedFrame(
        SharpDX.DataBox mapped,
        int width,
        int height,
        Format format,
        bool normalizeHdr)
    {
        return format switch
        {
            Format.B8G8R8A8_UNorm => CopyBgra8Frame(mapped, width, height),
            Format.R16G16B16A16_Float => CopyScRgbFrame(mapped, width, height, normalizeHdr),
            _ => throw new NotSupportedException($"Unsupported capture texture format {format}")
        };
    }

    private static Bitmap CopyBgra8Frame(
        SharpDX.DataBox mapped,
        int width,
        int height)
    {
        var bitmap = new Bitmap(width, height, DrawingPixelFormat.Format32bppArgb);
        var bits = bitmap.LockBits(
            new Rectangle(0, 0, width, height),
            ImageLockMode.WriteOnly,
            DrawingPixelFormat.Format32bppArgb);

        try
        {
            var source = mapped.DataPointer;
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

    private static Bitmap CopyScRgbFrame(
        SharpDX.DataBox mapped,
        int width,
        int height,
        bool normalizeHdr)
    {
        var sourceLength = checked(width * height * 8);
        var outputLength = checked(width * height * 4);
        var sourcePixels = ArrayPool<byte>.Shared.Rent(sourceLength);
        var outputPixels = ArrayPool<byte>.Shared.Rent(outputLength);

        try
        {
            var sourceRowBytes = width * 8;

            for (var y = 0; y < height; y++)
            {
                var source = IntPtr.Add(mapped.DataPointer, y * mapped.RowPitch);
                System.Runtime.InteropServices.Marshal.Copy(source, sourcePixels, y * sourceRowBytes, sourceRowBytes);
            }

            var toneMapper = normalizeHdr ? HdrToneMapper.Analyze(sourcePixels, width, height) : null;

            Parallel.For(0, height, y =>
            {
                var sourceRow = y * sourceRowBytes;
                var outputRow = y * width * 4;

                for (var x = 0; x < width; x++)
                {
                    var src = sourceRow + x * 8;
                    var r = ReadHalf(sourcePixels, src);
                    var g = ReadHalf(sourcePixels, src + 2);
                    var b = ReadHalf(sourcePixels, src + 4);
                    var a = ReadHalf(sourcePixels, src + 6);

                    toneMapper?.Map(ref r, ref g, ref b);

                    var dst = outputRow + x * 4;
                    outputPixels[dst] = HdrToneMapper.LinearToSrgbByte(b, x, y, 0);
                    outputPixels[dst + 1] = HdrToneMapper.LinearToSrgbByte(g, x, y, 1);
                    outputPixels[dst + 2] = HdrToneMapper.LinearToSrgbByte(r, x, y, 2);
                    outputPixels[dst + 3] =
                        (byte)Math.Clamp((int)Math.Round(Math.Clamp(a, 0f, 1f) * 255f), 0, 255);
                }
            });

            var bitmap = new Bitmap(width, height, DrawingPixelFormat.Format32bppArgb);
            try
            {
                var bits = bitmap.LockBits(
                    new Rectangle(0, 0, width, height),
                    ImageLockMode.WriteOnly,
                    DrawingPixelFormat.Format32bppArgb);

                try
                {
                    for (var y = 0; y < height; y++)
                    {
                        var destination = IntPtr.Add(bits.Scan0, y * bits.Stride);
                        System.Runtime.InteropServices.Marshal.Copy(outputPixels, y * width * 4, destination, width * 4);
                    }
                }
                finally
                {
                    bitmap.UnlockBits(bits);
                }

                return bitmap;
            }
            catch
            {
                bitmap.Dispose();
                throw;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(sourcePixels);
            ArrayPool<byte>.Shared.Return(outputPixels);
        }
    }

    private static float ReadHalf(byte[] row, int offset)
    {
        var bits = (ushort)(row[offset] | row[offset + 1] << 8);
        var value = (float)BitConverter.Int16BitsToHalf(unchecked((short)bits));
        return float.IsFinite(value) ? value : 0f;
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
}
