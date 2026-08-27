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
    private async void CaptureOnce_Click(object? sender, RoutedEventArgs e)
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

    private async void StartStop_Click(object? sender, RoutedEventArgs e)
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

        SetStatus($"Active — screenshots every {settings.IntervalMinutes:0.##} min — {notification}", true);
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
            while (!source.IsCancellationRequested)
            {
                await CaptureNowAsync(settings, true);
                await Task.Delay(TimeSpan.FromMinutes(settings.IntervalMinutes), source.Token);
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

            using var image = new ImageMagick.MagickImage(filePath);
            image.Format = ImageMagick.MagickFormat.Png;
            using var stream = new MemoryStream();
            image.Write(stream);
            stream.Position = 0;

            var bitmap = new Bitmap(stream);
            var previous = _clipboardBitmap;
            _clipboardBitmap = bitmap;

            try
            {
                await clipboard.SetBitmapAsync(bitmap);
                await clipboard.FlushAsync();
                previous?.Dispose();
                return null;
            }
            catch
            {
                _clipboardBitmap = previous;
                bitmap.Dispose();
                throw;
            }
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
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
