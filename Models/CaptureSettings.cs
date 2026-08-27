namespace WindowSnapper.Models;

public enum TargetMode
{
    ProcessExe,
    WindowTitle,
    WindowHandle
}

public enum CaptureBackend
{
    Auto = 0,
    WindowsGraphicsCapture = 1,
    DxgiDesktopDuplication = 2,
    PrintWindow = 3,
    ScreenCopy = 4,
    PortableWindow = 5,
    WaylandPortal = 6,
    LinuxImageMagickX11 = 7,
    LinuxXwdImageMagick = 8,
    LinuxGnomeScreenshot = 9,
    LinuxMaim = 11,
    LinuxScrot = 12,
    LinuxGrimblast = 13,
    LinuxXfceScreenshot = 14,
    LinuxSwayGrim = 15,
    LinuxHyprlandGrim = 16,
    LinuxHyprshot = 17,
    LinuxGrimSlurp = 18
}

public enum ImageFormatChoice
{
    Png,
    Jpeg,
    WebP,
    Avif
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
    public string TargetNativeId { get; set; } = string.Empty;
    public double IntervalMinutes { get; set; } = 5;
    public string OutputFolder { get; set; } = DefaultOutputFolder();
    public CaptureBackend Backend { get; set; } = CaptureBackend.Auto;
    public ImageFormatChoice ImageFormat { get; set; } = ImageFormatChoice.Png;
    public string FileNamePrefix { get; set; } = "capture";

    public double UiScale { get; set; } = 1.0;

    public NotificationTriggerMode? NotificationMode { get; set; }
    public int ScreenshotToastEvery { get; set; } = 1;
    public double ToastScale { get; set; } = 1;
    public double ToastDurationSeconds { get; set; } = 6.0;
    public double NotificationIntervalMinutes { get; set; } = 5;
    public string NotificationSoundPath { get; set; } = string.Empty;
    public bool NotificationSoundEnabled { get; set; } = true;
    public double NotificationSoundVolume { get; set; } = 0.75;
    public bool CopyLatestToClipboard { get; set; }
    public bool NormalizeHdrCaptures { get; set; } = true;
    public bool CaptureCursor { get; set; } = false;

    // Legacy fields used by older settings files.
    public double ResolutionScale { get; set; } = 1.0;
    public bool ScreenshotToastEnabled { get; set; } = true;
    public bool TimedNotificationEnabled { get; set; }
    public double ToastWidth { get; set; } = 344;
    public double ToastHeight { get; set; } = 82;

    private static string DefaultOutputFolder()
    {
        var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        if (string.IsNullOrWhiteSpace(pictures))
            pictures = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        return Path.Combine(pictures, "WindowSnapper");
    }
}
