using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using WindowSnapper.Models;
using WindowSnapper.Services;

namespace WindowSnapper;

public sealed partial class MainWindow
{
    private async void CaptureOnce_Click(object? sender, RoutedEventArgs e) =>
        await TriggerCaptureOnceAsync();

    private async void StartStop_Click(object? sender, RoutedEventArgs e) =>
        await ToggleCaptureAsync();

    private async Task TriggerCaptureOnceAsync()
    {
        var settings = ReadSettings(true, out var error);
        if (error.Length != 0)
        {
            SetStatus(error, false, true);
            return;
        }

        await SaveSettingsAsync(settings);
        await CaptureNowAsync(settings, _isRunning);
    }

    private async Task ToggleCaptureAsync()
    {
        if (_isRunning)
        {
            StopCapture();
            return;
        }

        var settings = ReadSettings(true, out var error);
        if (error.Length != 0)
        {
            SetStatus(error, false, true);
            return;
        }

        await SaveSettingsAsync(settings);
        StartCapture(settings);
    }

    private void StartCapture(CaptureSettings settings)
    {
        var captureSource = new CancellationTokenSource();
        var reminderSource = settings.NotificationMode == NotificationTriggerMode.TimedReminder
            ? new CancellationTokenSource()
            : null;

        _captureCts = captureSource;
        _reminderCts = reminderSource;
        _successfulCaptureCount = 0;
        _sessionTimer.Restart();
        _isRunning = true;
        StartStopButton.Content = "Stop capture";

        var notification = settings.NotificationMode switch
        {
            NotificationTriggerMode.EveryScreenshots => $"notify every {Math.Max(1, settings.ScreenshotToastEvery)} screenshots",
            NotificationTriggerMode.TimedReminder => $"reminder {settings.NotificationIntervalMinutes:0.##} min",
            _ => "notifications off"
        };

        SetStatus($"Active — first screenshot in {settings.IntervalMinutes:0.##} min — then every {settings.IntervalMinutes:0.##} min — {notification}", true);
        _ = RunCaptureLoopAsync(settings, captureSource);

        if (reminderSource is not null)
            _ = RunReminderLoopAsync(settings, reminderSource);
    }

    private void StopCapture()
    {
        _captureCts?.Cancel();
        _reminderCts?.Cancel();
        _captureCts = null;
        _reminderCts = null;
        _isRunning = false;
        _sessionTimer.Reset();
        StartStopButton.Content = "Start capture";
        SetStatus("Idle", false);
        _ = CaptureService.StopPlatformSessionAsync();
    }

    private void RestartReminder(CaptureSettings settings)
    {
        var previous = _reminderCts;
        _reminderCts = null;
        previous?.Cancel();

        if (!_isRunning || settings.NotificationMode != NotificationTriggerMode.TimedReminder)
            return;

        var source = new CancellationTokenSource();
        _reminderCts = source;
        _ = RunReminderLoopAsync(settings, source);
    }

    private async Task RunCaptureLoopAsync(CaptureSettings settings, CancellationTokenSource source)
    {
        try
        {
            var interval = TimeSpan.FromMinutes(settings.IntervalMinutes);
            using var timer = new PeriodicTimer(interval);

            while (!source.IsCancellationRequested
                   && await timer.WaitForNextTickAsync(source.Token))
            {
                await CaptureNowAsync(settings, true);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() => StopAfterCaptureError(ex.Message));
        }
        finally
        {
            if (ReferenceEquals(_captureCts, source))
                _captureCts = null;
            source.Dispose();
        }
    }

    private async Task RunReminderLoopAsync(CaptureSettings settings, CancellationTokenSource source)
    {
        try
        {
            while (!source.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromMinutes(settings.NotificationIntervalMinutes), source.Token);
                if (source.IsCancellationRequested)
                    break;

                await _captureLock.WaitAsync(source.Token);
                _captureLock.Release();

                var current = _settings;
                if (current.NotificationMode != NotificationTriggerMode.TimedReminder)
                    break;

                if (!current.ExclusiveFullscreenCompatibility)
                    ToastManager.ShowReminder(current.ToastScale, current.ToastDurationSeconds, _sessionTimer.Elapsed);

                if (current.NotificationSoundEnabled)
                {
                    var playback = NotificationSoundService.Play(current.NotificationSoundPath, current.NotificationSoundVolume);
                    if (!playback.Success)
                    {
                        await Dispatcher.UIThread.InvokeAsync(() =>
                            SetStatus($"Reminder sound error: {playback.Error}", _isRunning, true));
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
                SetStatus($"Reminder error: {ex.Message}", _isRunning, true));
        }
        finally
        {
            if (ReferenceEquals(_reminderCts, source))
                _reminderCts = null;
            source.Dispose();
        }
    }

    private async Task CaptureNowAsync(CaptureSettings settings, bool keepActiveStatus)
    {
        if (!await _captureLock.WaitAsync(0))
        {
            SetStatus("A capture is already in progress", keepActiveStatus);
            return;
        }

        try
        {
            // Safe mode means "do not touch windows while the game owns the display." Even our own toast counts.
            if (!settings.ExclusiveFullscreenCompatibility)
                ToastManager.HideCurrent();

            SetStatus("Capturing…", keepActiveStatus);

            var result = await CaptureService.CaptureAsync(settings);
            var active = keepActiveStatus && _isRunning;
            if (!result.Success)
            {
                SetStatus(result.Message, active, true);
                return;
            }

            var captureNumber = Interlocked.Increment(ref _successfulCaptureCount);
            var fileName = result.FilePath is null ? "Screenshot saved" : $"Saved {Path.GetFileName(result.FilePath)}";

            var currentSettings = _settings;
            if (currentSettings.CopyLatestToClipboard && !string.IsNullOrWhiteSpace(result.FilePath))
            {
                var clipboardError = await CopyCaptureToClipboardAsync(result.FilePath);
                if (clipboardError is not null)
                    SetStatus($"{fileName} — clipboard error: {clipboardError}", active, true);
                else
                    SetStatus($"{fileName} — copied to clipboard", active);
            }
            else
            {
                SetStatus(fileName, active);
            }

            var notification = _settings;
            var every = Math.Max(1, notification.ScreenshotToastEvery);
            if (notification.NotificationMode == NotificationTriggerMode.EveryScreenshots
                && captureNumber % every == 0)
            {
                if (!notification.ExclusiveFullscreenCompatibility)
                    ToastManager.ShowCaptureSaved(result.FilePath, notification.ToastScale, notification.ToastDurationSeconds);

                if (notification.NotificationSoundEnabled)
                {
                    var playback = NotificationSoundService.Play(notification.NotificationSoundPath, notification.NotificationSoundVolume);
                    if (!playback.Success)
                    {
                        SetStatus(
                            $"{fileName} — sound error: {playback.Error}",
                            active,
                            true);
                    }
                }
            }
        }
        finally
        {
            _captureLock.Release();
        }
    }

    private async Task<string?> CopyCaptureToClipboardAsync(string filePath)
    {
        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is null)
                return "clipboard is unavailable";

            // Keep codec fallback in the worker. AVIF can throw a fit here, and Visual Studio will still act surprised.
            var bitmap = await Task.Run(() => LoadClipboardBitmap(filePath));

            var previous = _clipboardBitmap;
            Exception? lastError = null;
            for (var attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    await clipboard.SetBitmapAsync(bitmap);
                    try
                    {
                        await clipboard.FlushAsync();
                    }
                    catch
                    {
                        // The bitmap already made it to the clipboard. If Flush() gets dramatic, don’t punish the screenshot for it.
                    }

                    _clipboardBitmap = bitmap;
                    previous?.Dispose();
                    return null;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    if (attempt < 2)
                        await Task.Delay(35 * (attempt + 1));
                }
            }

            bitmap.Dispose();
            return lastError?.Message ?? "clipboard rejected the image";
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    private static Bitmap LoadClipboardBitmap(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        var directDecode = extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
                           || extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
                           || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase);

        if (directDecode)
        {
            try
            {
                // PNG/JPEG is already boring 8-bit sRGB. No reason to pay ImageMagick twice for the same damn job.
                return new Bitmap(filePath);
            }
            catch (ArgumentException)
            {
                // Weird pixel layouts still exist because apparently peace was never an option. Fall back to the boring normalized path.
            }
            catch (InvalidOperationException)
            {
                // Decoder tantrum? Same boring fallback. Consistency is beautiful.
            }
        }

        // Clipboards love boring 8-bit sRGB PNG. Normalize the weird stuff in memory and leave the saved file the hell alone.
        using var image = new ImageMagick.MagickImage(filePath);
        image.AutoOrient();
        image.ColorSpace = ImageMagick.ColorSpace.sRGB;
        image.Depth = 8;
        image.Format = ImageMagick.MagickFormat.Png;
        image.Strip();

        var pngBytes = image.ToByteArray();
        using var stream = new MemoryStream(pngBytes, writable: false);
        return new Bitmap(stream);
    }

    private void StopAfterCaptureError(string message)
    {
        _reminderCts?.Cancel();
        _reminderCts = null;
        _captureCts = null;
        _isRunning = false;
        _sessionTimer.Reset();
        StartStopButton.Content = "Start capture";
        SetStatus(message, false, true);
        _ = CaptureService.StopPlatformSessionAsync();
    }

    private void SetStatus(string text, bool active, bool error = false)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => SetStatus(text, active, error));
            return;
        }

        StatusText.Text = text;
        StatusText.Foreground = new Avalonia.Media.SolidColorBrush(
            Avalonia.Media.Color.Parse(error ? "#E25B65" : active ? "#E8EAED" : "#A8ADB6"));
    }
}
