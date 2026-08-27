using Avalonia.Threading;

namespace WindowSnapper.Services;

public static class ToastManager
{
    private static readonly object StateLock = new();
    private static ToastWindow? _current;

    public static void ShowCaptureSaved(string? filePath, double scale, double durationSeconds)
    {
        var file = string.IsNullOrWhiteSpace(filePath) ? "Screenshot saved" : Path.GetFileName(filePath);
        Show("Screenshot saved", file, scale, durationSeconds);
    }

    public static void ShowReminder(double scale, double durationSeconds, TimeSpan elapsed)
    {
        var totalHours = (int)Math.Floor(elapsed.TotalHours);
        var runtime = $"{totalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";
        Show("Capture reminder", $"Current capture session has been running for {runtime}", scale, durationSeconds);
    }

    public static void ShowTest(double scale, double durationSeconds) =>
        Show("Test notification", "WindowSnapper notifications are ready", scale, durationSeconds);

    public static void HideCurrent()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            CloseCurrent();
            return;
        }

        Dispatcher.UIThread.Post(CloseCurrent);
    }

    private static void CloseCurrent()
    {
        ToastWindow? toast;
        lock (StateLock)
        {
            toast = _current;
            _current = null;
        }

        toast?.Close();
    }

    private static void Show(string title, string message, double scale, double durationSeconds)
    {
        Dispatcher.UIThread.Post(() =>
        {
            ToastWindow? previous;
            lock (StateLock)
            {
                previous = _current;
                _current = null;
            }
            previous?.Close();

            var toast = new ToastWindow(title, message, scale, durationSeconds);
            toast.Closed += (_, _) =>
            {
                lock (StateLock)
                {
                    if (ReferenceEquals(_current, toast))
                        _current = null;
                }
            };

            lock (StateLock)
                _current = toast;

            toast.Show();
            toast.StartAutoClose();
        });
    }
}
