using System.Diagnostics;
using System.Globalization;

namespace WindowSnapper.Platforms.Linux;

internal static class LinuxAudioPlayer
{
    private static readonly object StateLock = new();
    private static Process? _current;

    public static bool TryPlay(string path, double volume, out string? error)
    {
        error = null;
        var extension = Path.GetExtension(path).ToLowerInvariant();
        volume = Math.Clamp(double.IsFinite(volume) ? volume : 1.0, 0.0, 1.0);

        var percent = (int)Math.Round(volume * 100.0);
        var mpgScale = (int)Math.Round(volume * 32768.0);
        var pulseVolume = (int)Math.Round(volume * 65536.0);

        var candidates = new List<(string Name, string[] Args)>
        {
            ("ffplay", new[] { "-nodisp", "-autoexit", "-loglevel", "quiet", "-volume", percent.ToString(), path }),
            ("mpv", new[] { "--no-video", "--really-quiet", $"--volume={percent}", path })
        };

        if (extension == ".mp3")
            candidates.Add(("mpg123", new[] { "-q", "--scale", mpgScale.ToString(), path }));

        if (extension == ".wav")
        {
            candidates.Add(("pw-play", new[] { "--volume=" + volume.ToString("0.###", CultureInfo.InvariantCulture), path }));
            candidates.Add(("paplay", new[] { $"--volume={pulseVolume}", path }));
            if (volume >= 0.999)
                candidates.Add(("aplay", new[] { "-q", path }));
        }

        foreach (var candidate in candidates)
        {
            if (!CommandExists(candidate.Name))
                continue;

            try
            {
                Stop();
                var startInfo = new ProcessStartInfo(candidate.Name)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                };

                foreach (var argument in candidate.Args)
                    startInfo.ArgumentList.Add(argument);

                var process = Process.Start(startInfo);
                if (process is null)
                    continue;

                lock (StateLock)
                    _current = process;

                process.EnableRaisingEvents = true;
                process.Exited += (_, _) => ClearIfCurrent(process);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }
        }

        error ??= "No compatible audio player with volume control was found. Install ffmpeg/ffplay or mpv.";
        return false;
    }

    public static void Stop()
    {
        Process? process;
        lock (StateLock)
        {
            process = _current;
            _current = null;
        }

        if (process is null)
            return;

        try
        {
            if (!process.HasExited)
                process.Kill(true);
        }
        catch
        {
        }
        finally
        {
            process.Dispose();
        }
    }

    private static void ClearIfCurrent(Process process)
    {
        lock (StateLock)
        {
            if (ReferenceEquals(_current, process))
                _current = null;
        }

        process.Dispose();
    }

    private static bool CommandExists(string command)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        return path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(folder => Path.Combine(folder, command))
            .Any(File.Exists);
    }
}
