using System.Diagnostics;
using WindowSnapper.Models;
using WindowSnapper.Services;

namespace WindowSnapper.Platforms.Linux;

internal static class WaylandScreenCastSession
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static Process? _helper;
    private static string _lastError = string.Empty;
    private static bool? _captureCursor;

    public static async Task<CaptureResult> CaptureAsync(CaptureSettings settings)
    {
        await Gate.WaitAsync();
        try
        {
            var startError = await EnsureStartedAsync(settings.CaptureCursor);
            if (startError is not null)
                return new CaptureResult(false, startError);

            var temp = Path.Combine(Path.GetTempPath(), $"windowsnapper-wayland-{Guid.NewGuid():N}.png");
            try
            {
                await _helper!.StandardInput.WriteLineAsync($"CAPTURE\t{temp}");
                await _helper.StandardInput.FlushAsync();

                var response = await ReadResponseAsync(_helper, TimeSpan.FromSeconds(10));
                if (response is null)
                {
                    ResetHelper();
                    return new CaptureResult(false, "Wayland stream stopped responding");
                }

                if (response.StartsWith("ERR\t", StringComparison.Ordinal))
                    return new CaptureResult(false, response[4..]);

                if (!response.StartsWith("OK\t", StringComparison.Ordinal) || !File.Exists(temp))
                    return new CaptureResult(false, "Wayland stream returned an invalid capture response");

                var bytes = await File.ReadAllBytesAsync(temp);
                return ImageOutputService.Save(bytes, settings, "Wayland ScreenCast / PipeWire");
            }
            finally
            {
                TryDelete(temp);
            }
        }
        catch (Exception ex)
        {
            ResetHelper();
            return new CaptureResult(false, $"Wayland ScreenCast: {ex.Message}");
        }
        finally
        {
            Gate.Release();
        }
    }

    public static void Abort()
    {
        var helper = Interlocked.Exchange(ref _helper, null);
        _captureCursor = null;

        if (helper is null)
            return;

        TryKill(helper);
        helper.Dispose();
    }

    public static async Task StopAsync()
    {
        await Gate.WaitAsync();
        try
        {
            if (_helper is null)
                return;

            try
            {
                if (!_helper.HasExited)
                {
                    await _helper.StandardInput.WriteLineAsync("QUIT");
                    await _helper.StandardInput.FlushAsync();
                    await _helper.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2));
                }
            }
            catch
            {
                TryKill(_helper);
            }
            finally
            {
                ResetHelper();
            }
        }
        finally
        {
            Gate.Release();
        }
    }

    private static async Task<string?> EnsureStartedAsync(bool captureCursor)
    {
        if (_helper is { HasExited: false } && _captureCursor == captureCursor)
            return null;

        ResetHelper();

        if (!PlatformInfo.CommandExists("python3"))
            return "Wayland ScreenCast needs Python 3. Install python3 and try again.";

        var helperPath = Path.Combine(AppContext.BaseDirectory, "Platforms", "Linux", "wayland_capture_helper.py");
        if (!File.Exists(helperPath))
            return $"Wayland helper was not published: {helperPath}";

        _lastError = string.Empty;
        var startInfo = new ProcessStartInfo
        {
            FileName = "python3",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add(helperPath);
        startInfo.Environment["WINDOWSNAPPER_CAPTURE_CURSOR"] = captureCursor ? "1" : "0";

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
                _lastError = e.Data.Trim();
        };

        if (!process.Start())
        {
            process.Dispose();
            return "Could not start the Wayland ScreenCast helper";
        }

        process.BeginErrorReadLine();
        _helper = process;
        _captureCursor = captureCursor;

        var response = await ReadResponseAsync(process, TimeSpan.FromSeconds(90));
        if (response is null)
        {
            var detail = _lastError.Length == 0 ? "No response from the desktop portal" : _lastError;
            ResetHelper();
            return $"Wayland ScreenCast did not start: {detail}";
        }

        if (response == "READY")
            return null;

        if (response.StartsWith("ERR\t", StringComparison.Ordinal))
        {
            var message = response[4..];
            ResetHelper();
            return message;
        }

        ResetHelper();
        return $"Unexpected Wayland helper response: {response}";
    }

    private static async Task<string?> ReadResponseAsync(Process process, TimeSpan timeout)
    {
        try
        {
            return await process.StandardOutput.ReadLineAsync().WaitAsync(timeout);
        }
        catch (TimeoutException)
        {
            return null;
        }
    }

    private static void ResetHelper()
    {
        if (_helper is null)
            return;

        TryKill(_helper);
        _helper.Dispose();
        _helper = null;
        _captureCursor = null;
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
        }
    }

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
}
