using System.IO;
using NAudio.Wave;

namespace WindowSnapper.Services;

public static class NotificationSoundService
{
    public readonly record struct PlaybackResult(
        bool Success,
        string DisplayName,
        bool UsedFallback,
        string? Error = null);

    private sealed class PlaybackSession : IDisposable
    {
        private int _disposed;

        public PlaybackSession(IWavePlayer player, WaveStream reader)
        {
            Player = player;
            Reader = reader;
        }

        public IWavePlayer Player { get; }
        public WaveStream Reader { get; }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            try { Player.Dispose(); } catch { }
            try { Reader.Dispose(); } catch { }
        }
    }

    private static readonly object PlayLock = new();
    private static readonly object StateLock = new();
    private static PlaybackSession? _current;

    public static string DefaultSoundPath => Path.Combine(AppContext.BaseDirectory, "notif.mp3");

    public static PlaybackResult Play(string? customPath)
    {
        var hasCustomSound = !string.IsNullOrWhiteSpace(customPath);
        var requestedPath = hasCustomSound ? customPath! : DefaultSoundPath;

        if (TryPlay(requestedPath, out var error))
            return new PlaybackResult(true, DisplayName(customPath), false);

        if (hasCustomSound && TryPlay(DefaultSoundPath, out _))
        {
            return new PlaybackResult(
                true,
                "notif.mp3 (default)",
                true,
                error ?? "The selected sound could not be played.");
        }

        return new PlaybackResult(
            false,
            DisplayName(customPath),
            hasCustomSound,
            error ?? "The notification sound could not be played.");
    }

    public static void Stop()
    {
        PlaybackSession? session;
        lock (StateLock)
        {
            session = _current;
            _current = null;
        }

        StopSession(session);
    }

    public static string DisplayName(string? customPath)
    {
        return string.IsNullOrWhiteSpace(customPath)
            ? "notif.mp3 (default)"
            : Path.GetFileName(customPath);
    }

    private static bool TryPlay(string path, out string? error)
    {
        error = null;
        if (!File.Exists(path))
        {
            error = $"Sound file not found: {Path.GetFileName(path)}";
            return false;
        }

        lock (PlayLock)
        {
            PlaybackSession? session = null;

            try
            {
                var reader = CreateReader(path);
                var player = new WaveOutEvent { DesiredLatency = 100 };
                session = new PlaybackSession(player, reader);

                var finishedSession = session;
                player.PlaybackStopped += (_, _) => PlaybackFinished(finishedSession);
                player.Init(reader);

                PlaybackSession? previous;
                lock (StateLock)
                {
                    previous = _current;
                    _current = session;
                }

                StopSession(previous);
                player.Play();
                return true;
            }
            catch (Exception ex)
            {
                lock (StateLock)
                {
                    if (ReferenceEquals(_current, session))
                        _current = null;
                }

                session?.Dispose();
                error = ex.Message;
                return false;
            }
        }
    }

    private static WaveStream CreateReader(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".mp3" or ".wav" or ".aif" or ".aiff" => new AudioFileReader(path),
            _ => new MediaFoundationReader(path)
        };
    }

    private static void PlaybackFinished(PlaybackSession session)
    {
        lock (StateLock)
        {
            if (ReferenceEquals(_current, session))
                _current = null;
        }

        session.Dispose();
    }

    private static void StopSession(PlaybackSession? session)
    {
        if (session is null)
            return;

        try
        {
            session.Player.Stop();
        }
        catch
        {
        }

        session.Dispose();
    }
}
