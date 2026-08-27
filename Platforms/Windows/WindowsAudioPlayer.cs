using NAudio.Wave;

namespace WindowSnapper.Platforms.Windows;

internal static class WindowsAudioPlayer
{
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

    private static readonly object StateLock = new();
    private static PlaybackSession? _current;

    public static bool TryPlay(string path, double volume, out string? error)
    {
        error = null;
        PlaybackSession? session = null;

        try
        {
            var reader = CreateReader(path);
            var player = new WaveOutEvent
            {
                DesiredLatency = 100,
                Volume = (float)Math.Clamp(volume, 0.0, 1.0)
            };
            session = new PlaybackSession(player, reader);
            var finished = session;
            player.PlaybackStopped += (_, _) => PlaybackFinished(finished);
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

        try { session.Player.Stop(); } catch { }
        session.Dispose();
    }
}
