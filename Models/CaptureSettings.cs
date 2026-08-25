using System.IO;

namespace WindowSnapper.Models;

public enum TargetMode
{
    ProcessExe,
    WindowTitle,
    WindowHandle
}

public enum CaptureBackend
{
    Auto,
    WindowsGraphicsCapture,
    DxgiDesktopDuplication,
    PrintWindow,
    ScreenCopy
}

public enum ImageFormatChoice
{
    Png,
    Jpeg
}

public enum NotificationTriggerMode
{
    Off,
    EveryScreenshots,
    TimedReminder
}

public sealed class CaptureSettings
{
    public TargetMode TargetMode { get; set; } = TargetMode.ProcessExe;
    public string TargetValue { get; set; } = string.Empty;
    public double IntervalMinutes { get; set; } = 5;
    public string OutputFolder { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        "WindowSnapper");
    public CaptureBackend Backend { get; set; } = CaptureBackend.Auto;
    public ImageFormatChoice ImageFormat { get; set; } = ImageFormatChoice.Png;
    public string FileNamePrefix { get; set; } = "capture";

    public NotificationTriggerMode? NotificationMode { get; set; }
    public int ScreenshotToastEvery { get; set; } = 1;
    public double ToastScale { get; set; }

    public double NotificationIntervalMinutes { get; set; } = 5;
    public string NotificationSoundPath { get; set; } = string.Empty;
    public bool NotificationSoundEnabled { get; set; } = true;

    // Compatibility with settings saved by previous builds.
    public bool ScreenshotToastEnabled { get; set; } = true;
    public bool TimedNotificationEnabled { get; set; }

    // Kept for settings files written by older builds.
    public double ToastWidth { get; set; } = 344;
    public double ToastHeight { get; set; } = 82;
}
