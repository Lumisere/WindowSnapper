using System.IO;
using System.Windows;

namespace WindowSnapper.Services;

public static class ToastManager
{
    private static ToastWindow? _toast;

    public static void HideCurrent()
    {
        OnUiThread(() =>
        {
            if (_toast is null)
                return;

            var toast = _toast;
            _toast = null;

            if (toast.IsLoaded)
                toast.DismissImmediately();
            else
                toast.Close();
        });
    }

    public static void ShowCaptureSaved(string? filePath, double scale)
    {
        var message = string.IsNullOrWhiteSpace(filePath)
            ? "Screenshot saved"
            : Path.GetFileName(filePath);

        Show("Screenshot saved", message, scale);
    }
    
    public static void ShowReminder(double scale, TimeSpan elapsed)
    {
        var totalHours = (long)elapsed.TotalHours;
        var runtime = $"{totalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";
        Show("Capture reminder", $"Current capture session has been running for {runtime}", scale);
    }

    public static void ShowTest(double scale)
    {
        var percent = Math.Round(Math.Clamp(scale, 0.8, 2.0) * 100);
        Show("Toast preview", $"Notification size · {percent:0}%", scale);
    }

    private static void Show(string title, string message, double scale)
    {
        OnUiThread(() =>
        {
            HideCurrent();

            var toast = new ToastWindow(title, message, scale);
            _toast = toast;
            toast.Closed += (_, _) =>
            {
                if (ReferenceEquals(_toast, toast))
                    _toast = null;
            };
            toast.Show();
        });
    }

    private static void OnUiThread(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
            return;

        if (dispatcher.CheckAccess())
            action();
        else
            dispatcher.Invoke(action);
    }
}
