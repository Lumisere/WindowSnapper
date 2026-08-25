using System.IO;
using System.Windows;
using WindowSnapper.Models;
using WindowSnapper.Services;

namespace WindowSnapper;

public partial class MainWindow
{
    private async void CaptureOnceClick(object sender, RoutedEventArgs e)
    {
        var settings = ReadSettings(true, out var error);
        if (error.Length != 0)
        {
            SetCaptureStatus(error, false, true);
            return;
        }

        await SaveSettingsAsync(settings);
        await CaptureAsync(settings, _isRunning);
    }

    private async void StartStopClick(object sender, RoutedEventArgs e)
    {
        if (_isRunning)
        {
            StopCapture();
            return;
        }

        var settings = ReadSettings(true, out var error);
        if (error.Length != 0)
        {
            SetCaptureStatus(error, false, true);
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

        var notificationText = settings.NotificationMode switch
        {
            NotificationTriggerMode.EveryScreenshots => $"notify every {Math.Max(1, settings.ScreenshotToastEvery)} screenshots",
            NotificationTriggerMode.TimedReminder => $"reminder {settings.NotificationIntervalMinutes:0.##} min",
            _ => "notifications off"
        };

        SetCaptureStatus(
            $"Active — screenshots {settings.IntervalMinutes:0.##} min · {notificationText}",
            true);

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
        SetCaptureStatus("Idle", false);
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
                await CaptureAsync(settings, true);
                await Task.Delay(TimeSpan.FromMinutes(settings.IntervalMinutes), source.Token);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Dispatcher.Invoke(() => StopAfterCaptureError(ex.Message));
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

                // Don't let a reminder appear in the middle of a desktop-based capture, or dont I dont care but this prevents it
                await _captureLock.WaitAsync(source.Token);
                _captureLock.Release();

                if (source.IsCancellationRequested)
                    break;

                var reminderSettings = _settings;
                if (reminderSettings.NotificationMode != NotificationTriggerMode.TimedReminder)
                    break;

                var elapsed = _sessionTimer.Elapsed;
                ToastManager.ShowReminder(reminderSettings.ToastScale, elapsed);

                if (reminderSettings.NotificationSoundEnabled)
                {
                    var playback = NotificationSoundService.Play(reminderSettings.NotificationSoundPath);
                    if (!playback.Success)
                    {
                        Dispatcher.Invoke(() =>
                            SetCaptureStatus($"Reminder sound error: {playback.Error}", _isRunning, true));
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Dispatcher.Invoke(() =>
                SetCaptureStatus($"Reminder error: {ex.Message}", _isRunning, true));
        }
        finally
        {
            if (ReferenceEquals(_reminderCts, source))
                _reminderCts = null;

            source.Dispose();
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
        SetCaptureStatus(message, false, true);
    }

    private async Task CaptureAsync(CaptureSettings settings, bool keepActiveStatus)
    {
        if (!await _captureLock.WaitAsync(0))
        {
            SetCaptureStatus("A capture is already in progress", keepActiveStatus);
            return;
        }

        try
        {
            ToastManager.HideCurrent();
            SetCaptureStatus("Capturing…", keepActiveStatus);

            var result = await CaptureService.CaptureAsync(settings);
            var active = keepActiveStatus && _isRunning;

            if (!result.Success)
            {
                SetCaptureStatus(result.Message, active, true);
                return;
            }

            var captureNumber = Interlocked.Increment(ref _successfulCaptureCount);
            var fileName = result.FilePath is null
                ? "Screenshot saved"
                : $"Saved {Path.GetFileName(result.FilePath)}";

            SetCaptureStatus(fileName, active);

            var notificationSettings = _settings;
            var toastEvery = Math.Max(1, notificationSettings.ScreenshotToastEvery);
            if (notificationSettings.NotificationMode == NotificationTriggerMode.EveryScreenshots
                && captureNumber % toastEvery == 0)
            {
                ToastManager.ShowCaptureSaved(result.FilePath, notificationSettings.ToastScale);

                if (notificationSettings.NotificationSoundEnabled)
                {
                    var playback = NotificationSoundService.Play(notificationSettings.NotificationSoundPath);
                    if (!playback.Success)
                    {
                        SetCaptureStatus(
                            $"Saved {Path.GetFileName(result.FilePath)} · sound error: {playback.Error}",
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

    private void SetCaptureStatus(string text, bool active, bool error = false)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => SetCaptureStatus(text, active, error));
            return;
        }

        CaptureStatusText.Text = text;
    }
}
