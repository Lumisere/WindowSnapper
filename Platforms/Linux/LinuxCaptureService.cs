using System.Diagnostics;
using System.Text.Json;
using Shutter;
using Shutter.Enums;
using Shutter.Models;
using WindowSnapper.Models;
using WindowSnapper.Services;
using ShutterImageFormat = Shutter.Enums.ImageFormat;

namespace WindowSnapper.Platforms.Linux;

internal static class LinuxCaptureService
{
    public static Task<CaptureResult> CaptureAsync(CaptureSettings settings, WindowInfo? target)
    {
        var handle = target?.Handle ?? 0;

        return settings.Backend switch
        {
            CaptureBackend.WaylandPortal => WaylandScreenCastSession.CaptureAsync(settings),
            CaptureBackend.PortableWindow => CaptureAutoX11Async(settings, handle),
            CaptureBackend.LinuxImageMagickX11 => CaptureImageMagickWindowAsync(settings, handle),
            CaptureBackend.LinuxXwdImageMagick => CaptureXwdWindowAsync(settings, handle),
            CaptureBackend.LinuxGnomeScreenshot => CaptureGnomeWindowAsync(settings, handle),
            CaptureBackend.LinuxMaim => CaptureMaimWindowAsync(settings, handle),
            CaptureBackend.LinuxScrot => CaptureScrotWindowAsync(settings, handle),
            CaptureBackend.LinuxGrimblast => CaptureGrimblastAsync(settings),
            CaptureBackend.LinuxXfceScreenshot => CaptureXfceWindowAsync(settings, handle),
            CaptureBackend.LinuxSwayGrim => CaptureSwayWindowAsync(settings),
            CaptureBackend.LinuxHyprlandGrim => CaptureHyprlandWindowAsync(settings),
            CaptureBackend.LinuxHyprshot => CaptureHyprshotAsync(settings),
            CaptureBackend.LinuxGrimSlurp => CaptureGrimSlurpAsync(settings),
            CaptureBackend.Auto when PlatformInfo.IsWayland && handle != 0 => CaptureAutoX11Async(settings, handle),
            CaptureBackend.Auto when PlatformInfo.IsWayland => WaylandScreenCastSession.CaptureAsync(settings),
            _ => CaptureAutoX11Async(settings, handle)
        };
    }

    private static async Task<CaptureResult> CaptureAutoX11Async(CaptureSettings settings, nint handle)
    {
        if (!PlatformInfo.HasX11)
            return new CaptureResult(false, "This window is not available through X11/XWayland");

        if (handle == 0)
            return new CaptureResult(false, "Choose an X11/XWayland window first");

        var attempts = new List<string>();

        if (PlatformInfo.CommandExists("magick") || PlatformInfo.CommandExists("import"))
        {
            var result = await CaptureImageMagickWindowAsync(settings, handle);
            if (result.Success)
                return result;
            attempts.Add(result.Message);
        }

        if (PlatformInfo.CommandExists("xwd") && HasImageMagick())
        {
            var result = await CaptureXwdWindowAsync(settings, handle);
            if (result.Success)
                return result;
            attempts.Add(result.Message);
        }

        if (PlatformInfo.CommandExists("maim"))
        {
            var result = await CaptureMaimWindowAsync(settings, handle);
            if (result.Success)
                return result;
            attempts.Add(result.Message);
        }

        if (PlatformInfo.CommandExists("scrot"))
        {
            var result = await CaptureScrotWindowAsync(settings, handle);
            if (result.Success)
                return result;
            attempts.Add(result.Message);
        }

        if (PlatformInfo.IsXfce
            && PlatformInfo.CommandExists("xfce4-screenshooter")
            && PlatformInfo.CommandExists("xdotool"))
        {
            var result = await CaptureXfceWindowAsync(settings, handle);
            if (result.Success)
                return result;
            attempts.Add(result.Message);
        }

        var detail = attempts.Count == 0
            ? "Install ImageMagick, maim, or scrot for X11/XWayland window capture."
            : string.Join(" | ", attempts);
        return new CaptureResult(false, $"X11/XWayland capture failed. {detail}");
    }

    private static CaptureResult CaptureShutterWindow(CaptureSettings settings, nint handle)
    {
        if (!PlatformInfo.HasX11)
            return new CaptureResult(false, "X11/XWayland is not available in this session");

        try
        {
            var service = new ShutterService();
            var options = new ScreenshotOptions
            {
                Target = CaptureTarget.Window,
                WindowHandle = handle,
                IncludeBorder = true,
                IncludeShadow = false,
                Format = ShutterImageFormat.Png,
                Fallback = FallbackBehavior.ThrowException
            };

            return ImageOutputService.Save(service.TakeScreenshot(options), settings, "X11 direct capture");
        }
        catch (Exception ex)
        {
            return new CaptureResult(false, $"X11 direct capture: {ex.Message}");
        }
    }

    private static async Task<CaptureResult> CaptureImageMagickWindowAsync(CaptureSettings settings, nint handle)
    {
        if (!PlatformInfo.HasX11)
            return new CaptureResult(false, "ImageMagick window capture requires X11/XWayland");

        var temp = TempPath("png");
        try
        {
            var id = HexWindowId(handle);
            CommandResult result;

            if (PlatformInfo.CommandExists("magick"))
                result = await RunAsync("magick", new[] { "import", "-silent", "-window", id, temp });
            else if (PlatformInfo.CommandExists("import"))
                result = await RunAsync("import", new[] { "-silent", "-window", id, temp });
            else
                return new CaptureResult(false, "ImageMagick was not found in PATH");

            return await SaveToolOutputAsync(result, temp, settings, "ImageMagick X11");
        }
        finally
        {
            TryDelete(temp);
        }
    }

    private static async Task<CaptureResult> CaptureXwdWindowAsync(CaptureSettings settings, nint handle)
    {
        if (!PlatformInfo.HasX11)
            return new CaptureResult(false, "xwd capture requires X11/XWayland");
        if (!PlatformInfo.CommandExists("xwd"))
            return new CaptureResult(false, "xwd was not found in PATH");
        if (!HasImageMagick())
            return new CaptureResult(false, "ImageMagick was not found in PATH");

        var dumpPath = TempPath("xwd");
        var imagePath = TempPath("png");
        try
        {
            var dump = await RunAsync("xwd", new[] { "-silent", "-id", HexWindowId(handle), "-out", dumpPath });
            if (!dump.Success || !File.Exists(dumpPath))
                return ToolFailed("xwd", dump);

            var convert = PlatformInfo.CommandExists("magick")
                ? await RunAsync("magick", new[] { dumpPath, imagePath })
                : await RunAsync("convert", new[] { dumpPath, imagePath });

            return await SaveToolOutputAsync(convert, imagePath, settings, "xwd + ImageMagick");
        }
        finally
        {
            TryDelete(dumpPath);
            TryDelete(imagePath);
        }
    }

    private static async Task<CaptureResult> CaptureGnomeWindowAsync(CaptureSettings settings, nint handle)
    {
        if (!PlatformInfo.CommandExists("gnome-screenshot"))
            return new CaptureResult(false, "gnome-screenshot was not found in PATH");

        var activation = await ActivateIfNeededAsync(handle);
        if (!activation.Success)
            return new CaptureResult(false, activation.Error);

        var temp = TempPath("png");
        try
        {
            await SettleAfterActivationAsync(handle);
            var args = new List<string> { "-w", "-f", temp };
            if (settings.CaptureCursor)
                args.Insert(1, "-p");
            var result = await RunAsync("gnome-screenshot", args);
            return await SaveToolOutputAsync(result, temp, settings, "GNOME Screenshot");
        }
        finally
        {
            TryDelete(temp);
        }
    }

    private static async Task<CaptureResult> CaptureXfceWindowAsync(CaptureSettings settings, nint handle)
    {
        if (!PlatformInfo.HasX11)
            return new CaptureResult(false, "XFCE Screenshooter window capture requires X11/XWayland");
        if (!PlatformInfo.CommandExists("xfce4-screenshooter"))
            return new CaptureResult(false, "xfce4-screenshooter was not found in PATH");

        var activation = await ActivateIfNeededAsync(handle);
        if (!activation.Success)
            return new CaptureResult(false, activation.Error);

        var temp = TempPath("png");
        try
        {
            await SettleAfterActivationAsync(handle);
            var args = new List<string> { "--window", "--save", temp };
            if (settings.CaptureCursor)
                args.Insert(1, "--mouse");
            var result = await RunAsync("xfce4-screenshooter", args);
            return await SaveToolOutputAsync(result, temp, settings, "XFCE Screenshooter");
        }
        finally
        {
            TryDelete(temp);
        }
    }

    private static async Task<CaptureResult> CaptureMaimWindowAsync(CaptureSettings settings, nint handle)
    {
        if (!PlatformInfo.HasX11)
            return new CaptureResult(false, "maim capture requires X11/XWayland");
        if (!PlatformInfo.CommandExists("maim"))
            return new CaptureResult(false, "maim was not found in PATH");

        var temp = TempPath("png");
        try
        {
            var args = new List<string> { "-i", HexWindowId(handle) };
            if (!settings.CaptureCursor)
                args.Add("-u");
            args.Add(temp);
            var result = await RunAsync("maim", args);
            return await SaveToolOutputAsync(result, temp, settings, "maim X11");
        }
        finally
        {
            TryDelete(temp);
        }
    }

    private static async Task<CaptureResult> CaptureScrotWindowAsync(CaptureSettings settings, nint handle)
    {
        if (!PlatformInfo.HasX11)
            return new CaptureResult(false, "scrot capture requires X11/XWayland");
        if (!PlatformInfo.CommandExists("scrot"))
            return new CaptureResult(false, "scrot was not found in PATH");

        var temp = TempPath("png");
        try
        {
            var args = new List<string> { "-z" };
            if (settings.CaptureCursor)
                args.Add("-p");
            args.AddRange(new[] { "-w", HexWindowId(handle), "-F", temp });
            var result = await RunAsync("scrot", args);
            return await SaveToolOutputAsync(result, temp, settings, "scrot X11");
        }
        finally
        {
            TryDelete(temp);
        }
    }

    private static async Task<CaptureResult> CaptureGrimblastAsync(CaptureSettings settings)
    {
        if (!PlatformInfo.CommandExists("grimblast"))
            return new CaptureResult(false, "grimblast was not found in PATH");

        var temp = TempPath("png");
        try
        {
            var args = new List<string>();
            if (settings.CaptureCursor)
                args.Add("-c");
            args.AddRange(new[] { "save", "active", temp });
            var result = await RunAsync("grimblast", args);
            return await SaveToolOutputAsync(result, temp, settings, "Hyprland grimblast");
        }
        finally
        {
            TryDelete(temp);
        }
    }

    private static async Task<CaptureResult> CaptureSwayWindowAsync(CaptureSettings settings)
    {
        if (!PlatformInfo.CommandExists("swaymsg") || !PlatformInfo.CommandExists("grim"))
            return new CaptureResult(false, "Sway focused-window capture needs swaymsg and grim");

        var tree = await RunAsync("swaymsg", new[] { "-t", "get_tree", "-r" });
        if (!tree.Success)
            return ToolFailed("swaymsg", tree);

        if (!TryFindFocusedRect(tree.Output, out var rect))
            return new CaptureResult(false, "Could not find Sway's focused window geometry");

        return await CaptureGrimRectAsync(settings, rect, "Sway + grim");
    }

    private static async Task<CaptureResult> CaptureHyprlandWindowAsync(CaptureSettings settings)
    {
        if (!PlatformInfo.CommandExists("hyprctl") || !PlatformInfo.CommandExists("grim"))
            return new CaptureResult(false, "Hyprland focused-window capture needs hyprctl and grim");

        var activeWindow = await RunAsync("hyprctl", new[] { "activewindow", "-j" });
        if (!activeWindow.Success)
            return ToolFailed("hyprctl", activeWindow);

        if (!TryReadHyprlandRect(activeWindow.Output, out var rect))
            return new CaptureResult(false, "Could not read Hyprland's active window geometry");

        return await CaptureGrimRectAsync(settings, rect, "Hyprland + grim");
    }

    private static async Task<CaptureResult> CaptureHyprshotAsync(CaptureSettings settings)
    {
        if (!PlatformInfo.CommandExists("hyprshot"))
            return new CaptureResult(false, "hyprshot was not found in PATH");

        var temp = TempPath("png");
        try
        {
            var folder = Path.GetDirectoryName(temp) ?? Path.GetTempPath();
            var fileName = Path.GetFileName(temp);
            var result = await RunAsync("hyprshot", new[]
            {
                "-m", "window",
                "-m", "active",
                "-o", folder,
                "-f", fileName,
                "-s"
            });

            return await SaveToolOutputAsync(result, temp, settings, "Hyprshot active window");
        }
        finally
        {
            TryDelete(temp);
        }
    }

    private static async Task<CaptureResult> CaptureGrimSlurpAsync(CaptureSettings settings)
    {
        if (!PlatformInfo.CommandExists("grim") || !PlatformInfo.CommandExists("slurp"))
            return new CaptureResult(false, "grim + slurp capture needs both grim and slurp");

        var selection = await RunAsync("slurp", Array.Empty<string>(), TimeSpan.FromSeconds(60));
        if (!selection.Success)
            return ToolFailed("slurp", selection);

        var geometry = selection.Output.Trim();
        if (geometry.Length == 0)
            return new CaptureResult(false, "No Wayland region was selected");

        var temp = TempPath("png");
        try
        {
            var args = new List<string>();
            if (settings.CaptureCursor)
                args.Add("-c");
            args.AddRange(new[] { "-g", geometry, temp });
            var result = await RunAsync("grim", args);
            return await SaveToolOutputAsync(result, temp, settings, "grim + slurp");
        }
        finally
        {
            TryDelete(temp);
        }
    }

    private static async Task<CaptureResult> CaptureGrimRectAsync(CaptureSettings settings, CaptureRect rect, string method)
    {
        var temp = TempPath("png");
        try
        {
            var args = new List<string>();
            if (settings.CaptureCursor)
                args.Add("-c");
            args.AddRange(new[] { "-g", rect.ToGeometry(), temp });
            var result = await RunAsync("grim", args);
            return await SaveToolOutputAsync(result, temp, settings, method);
        }
        finally
        {
            TryDelete(temp);
        }
    }

    private static async Task<CaptureResult> SaveToolOutputAsync(
        CommandResult command,
        string imagePath,
        CaptureSettings settings,
        string method)
    {
        if (!command.Success || !File.Exists(imagePath))
            return ToolFailed(method, command);

        var data = await File.ReadAllBytesAsync(imagePath);
        return ImageOutputService.Save(data, settings, method);
    }

    private static CaptureResult ToolFailed(string tool, CommandResult command)
    {
        var reason = string.IsNullOrWhiteSpace(command.Error)
            ? "the command did not create an image"
            : command.Error;
        return new CaptureResult(false, $"{tool} failed: {reason}");
    }

    private static async Task<CommandResult> ActivateIfNeededAsync(nint handle)
    {
        if (handle == 0)
            return CommandResult.Ok();

        if (!PlatformInfo.CommandExists("xdotool"))
            return CommandResult.Fail("This method needs xdotool to focus the selected X11 window");

        return await RunAsync("xdotool", new[] { "windowactivate", "--sync", handle.ToInt64().ToString() });
    }

    private static Task SettleAfterActivationAsync(nint handle) =>
        handle == 0 ? Task.CompletedTask : Task.Delay(120);

    private static async Task<CommandResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        TimeSpan? timeout = null)
    {
        try
        {
            var startInfo = new ProcessStartInfo(fileName)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };

            foreach (var argument in arguments)
                startInfo.ArgumentList.Add(argument);

            using var process = Process.Start(startInfo);
            if (process is null)
                return CommandResult.Fail($"Could not start {fileName}");

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            using var timeoutSource = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(30));

            try
            {
                await process.WaitForExitAsync(timeoutSource.Token);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(true); } catch { }
                return CommandResult.Fail($"{fileName} timed out");
            }

            var output = await outputTask;
            var error = await errorTask;
            return process.ExitCode == 0
                ? CommandResult.Ok(output)
                : CommandResult.Fail(string.IsNullOrWhiteSpace(error) ? $"exit code {process.ExitCode}" : error.Trim(), output);
        }
        catch (Exception ex)
        {
            return CommandResult.Fail(ex.Message);
        }
    }

    private static bool TryFindFocusedRect(string json, out CaptureRect rect)
    {
        rect = default;
        try
        {
            using var document = JsonDocument.Parse(json);
            return TryFindFocusedRect(document.RootElement, out rect);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryFindFocusedRect(JsonElement element, out CaptureRect rect)
    {
        rect = default;
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("focused", out var focused)
                && focused.ValueKind == JsonValueKind.True
                && element.TryGetProperty("rect", out var rectElement)
                && TryReadRect(rectElement, out rect))
            {
                return true;
            }

            foreach (var property in element.EnumerateObject())
            {
                if (TryFindFocusedRect(property.Value, out rect))
                    return true;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (TryFindFocusedRect(item, out rect))
                    return true;
            }
        }

        return false;
    }

    private static bool TryReadHyprlandRect(string json, out CaptureRect rect)
    {
        rect = default;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!root.TryGetProperty("at", out var at)
                || !root.TryGetProperty("size", out var size)
                || at.ValueKind != JsonValueKind.Array
                || size.ValueKind != JsonValueKind.Array
                || at.GetArrayLength() < 2
                || size.GetArrayLength() < 2)
            {
                return false;
            }

            var x = at[0].GetInt32();
            var y = at[1].GetInt32();
            var width = size[0].GetInt32();
            var height = size[1].GetInt32();
            if (width <= 0 || height <= 0)
                return false;

            rect = new CaptureRect(x, y, width, height);
            return true;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return false;
        }
    }

    private static bool TryReadRect(JsonElement element, out CaptureRect rect)
    {
        rect = default;
        if (!TryGetInt(element, "x", out var x)
            || !TryGetInt(element, "y", out var y)
            || !TryGetInt(element, "width", out var width)
            || !TryGetInt(element, "height", out var height)
            || width <= 0
            || height <= 0)
        {
            return false;
        }

        rect = new CaptureRect(x, y, width, height);
        return true;
    }

    private static bool TryGetInt(JsonElement element, string propertyName, out int value)
    {
        value = 0;
        return element.TryGetProperty(propertyName, out var property)
            && property.TryGetInt32(out value);
    }

    private static bool HasImageMagick() =>
        PlatformInfo.CommandExists("magick") || PlatformInfo.CommandExists("convert");

    private static string HexWindowId(nint handle) => $"0x{handle.ToInt64():X}";

    private static string TempPath(string extension) =>
        Path.Combine(Path.GetTempPath(), $"windowsnapper-{Guid.NewGuid():N}.{extension}");

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }

    private readonly record struct CaptureRect(int X, int Y, int Width, int Height)
    {
        public string ToGeometry() => $"{X},{Y} {Width}x{Height}";
    }

    private readonly record struct CommandResult(bool Success, string Output, string Error)
    {
        public static CommandResult Ok(string output = "") => new(true, output, string.Empty);
        public static CommandResult Fail(string error, string output = "") => new(false, output, error);
    }
}
