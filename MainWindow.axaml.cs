using Avalonia;
using System.Diagnostics;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.Platform.Storage;
using WindowSnapper.Models;
using WindowSnapper.Services;

namespace WindowSnapper;

public sealed partial class MainWindow : Window
{
    private sealed record ComboOption<T>(T Value, string Label)
    {
        public override string ToString() => Label;
    }

    private const double MinUiScale = 0.8;
    private const double MaxUiScale = 1.25;
    private const double BaseWindowWidth = 1000;
    private const double BaseWindowHeight = 760;

    private readonly SemaphoreSlim _captureLock = new(1, 1);
    private readonly Stopwatch _sessionTimer = new();
    private CancellationTokenSource? _captureCts;
    private CancellationTokenSource? _reminderCts;
    private SettingsWindow? _settingsWindow;
    private Avalonia.Media.Imaging.Bitmap? _clipboardBitmap;
    private CaptureSettings _settings = new();
    private bool _isRunning;
    private long _successfulCaptureCount;
    private bool _loading = true;
    private bool _ignoreWindowSelection;
    private string _selectedNativeWindowId = string.Empty;
    private string _selectedNativeTargetValue = string.Empty;
    private bool _restoringWindowBounds;
    private bool _shutdownInProgress;
    private bool _allowClose;
    private double _normalWidth = BaseWindowWidth;
    private double _normalHeight = BaseWindowHeight;
    private PixelPoint _normalPosition;

    public MainWindow()
    {
        InitializeComponent();
        PopulateChoices();

        Resized += (_, _) =>
        {
            if (WindowState == WindowState.Normal && !_restoringWindowBounds)
                RememberNormalBounds();
        };
        PositionChanged += (_, _) =>
        {
            if (WindowState == WindowState.Normal && !_restoringWindowBounds)
                RememberNormalBounds();
        };
    }

    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        _settings = await SettingsStore.LoadAsync();
        ApplySettings(_settings);
        FitToWorkingArea();
        RememberNormalBounds();
        RefreshWindowList(false);
        UpdateTargetHelp();
        UpdateBackendHelp();
        UpdateNotificationUi();
        _loading = false;
    }


    private void FitToWorkingArea()
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null)
            return;

        var maxWidth = Math.Max(820, screen.WorkingArea.Width / screen.Scaling - 24);
        var maxHeight = Math.Max(690, screen.WorkingArea.Height / screen.Scaling - 24);
        MaxWidth = maxWidth;
        MaxHeight = maxHeight;

        var scale = NormalizeUiScale(_settings.UiScale);
        Width = Math.Min(BaseWindowWidth * scale, maxWidth);
        Height = Math.Min(BaseWindowHeight * scale, maxHeight);
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            _ = ShutdownAsync();
            return;
        }

        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _captureCts?.Cancel();
        _reminderCts?.Cancel();
        CaptureService.AbortPlatformSession();
        base.OnClosed(e);
    }

    private async Task ShutdownAsync()
    {
        if (_shutdownInProgress)
            return;

        _shutdownInProgress = true;

        _captureCts?.Cancel();
        _reminderCts?.Cancel();
        _captureCts = null;
        _reminderCts = null;
        _sessionTimer.Stop();
        NotificationSoundService.Stop();
        CaptureService.AbortPlatformSession();

        if (_settingsWindow is not null)
        {
            _settingsWindow.Close();
            _settingsWindow = null;
        }

        try
        {
            var settings = ReadSettings(false, out _);
            await SettingsStore.SaveAsync(settings).WaitAsync(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // Closing the app should never be held up by a settings write.
        }

        _clipboardBitmap?.Dispose();
        _clipboardBitmap = null;
        _allowClose = true;

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
        else
            Close();
    }

    private void PopulateChoices()
    {
        TargetModeCombo.ItemsSource = new[]
        {
            new ComboOption<TargetMode>(TargetMode.ProcessExe, PlatformInfo.IsWindows ? "Process / EXE name" : "Process name"),
            new ComboOption<TargetMode>(TargetMode.WindowTitle, "Window title"),
            new ComboOption<TargetMode>(
                TargetMode.WindowHandle,
                PlatformInfo.IsWindows
                    ? "Window handle (HWND)"
                    : "X11 window ID")
        };

        var backends = new List<ComboOption<CaptureBackend>>
        {
            new(CaptureBackend.Auto, "Auto")
        };

        if (PlatformInfo.IsWindows)
        {
            backends.Add(new ComboOption<CaptureBackend>(CaptureBackend.WindowsGraphicsCapture, "Windows Graphics Capture"));
            backends.Add(new ComboOption<CaptureBackend>(CaptureBackend.DxgiDesktopDuplication, "DXGI Desktop Duplication"));
            backends.Add(new ComboOption<CaptureBackend>(CaptureBackend.PrintWindow, "PrintWindow"));
            backends.Add(new ComboOption<CaptureBackend>(CaptureBackend.ScreenCopy, "Screen Copy"));
            backends.Add(new ComboOption<CaptureBackend>(CaptureBackend.PortableWindow, "Portable window capture"));
        }
        else
        {
            if (PlatformInfo.HasX11)
            {
                var hasMagick = PlatformInfo.CommandExists("magick") || PlatformInfo.CommandExists("import");
                if (hasMagick)
                    backends.Add(new ComboOption<CaptureBackend>(CaptureBackend.LinuxImageMagickX11, "ImageMagick X11 window"));

                if (PlatformInfo.CommandExists("xwd")
                    && (PlatformInfo.CommandExists("magick") || PlatformInfo.CommandExists("convert")))
                {
                    backends.Add(new ComboOption<CaptureBackend>(CaptureBackend.LinuxXwdImageMagick, "xwd + ImageMagick"));
                }

                if (!PlatformInfo.IsWayland && PlatformInfo.IsGnome && PlatformInfo.CommandExists("gnome-screenshot") && PlatformInfo.CommandExists("xdotool"))
                    backends.Add(new ComboOption<CaptureBackend>(CaptureBackend.LinuxGnomeScreenshot, "GNOME Screenshot (active window)"));
                if (PlatformInfo.CommandExists("xfce4-screenshooter") && PlatformInfo.CommandExists("xdotool"))
                    backends.Add(new ComboOption<CaptureBackend>(CaptureBackend.LinuxXfceScreenshot, "XFCE Screenshooter (active window)"));
                if (PlatformInfo.CommandExists("maim"))
                    backends.Add(new ComboOption<CaptureBackend>(CaptureBackend.LinuxMaim, "maim X11 window"));
                if (PlatformInfo.CommandExists("scrot"))
                    backends.Add(new ComboOption<CaptureBackend>(CaptureBackend.LinuxScrot, "scrot X11 window"));
            }

            if (PlatformInfo.IsWayland)
            {
                backends.Add(new ComboOption<CaptureBackend>(CaptureBackend.WaylandPortal, "Wayland window stream (PipeWire)"));

                if (PlatformInfo.IsSway && PlatformInfo.CommandExists("swaymsg") && PlatformInfo.CommandExists("grim"))
                    backends.Add(new ComboOption<CaptureBackend>(CaptureBackend.LinuxSwayGrim, "Sway + grim (focused window)"));
                if (PlatformInfo.IsHyprland && PlatformInfo.CommandExists("hyprctl") && PlatformInfo.CommandExists("grim"))
                    backends.Add(new ComboOption<CaptureBackend>(CaptureBackend.LinuxHyprlandGrim, "Hyprland + grim (active window)"));
                if (PlatformInfo.IsHyprland && PlatformInfo.CommandExists("hyprshot"))
                    backends.Add(new ComboOption<CaptureBackend>(CaptureBackend.LinuxHyprshot, "Hyprshot (active window)"));
                if (PlatformInfo.IsHyprland && PlatformInfo.CommandExists("grimblast"))
                    backends.Add(new ComboOption<CaptureBackend>(CaptureBackend.LinuxGrimblast, "grimblast (active window)"));
            }
        }

        BackendCombo.ItemsSource = backends;
        FormatCombo.ItemsSource = new[]
        {
            new ComboOption<ImageFormatChoice>(ImageFormatChoice.Png, "PNG"),
            new ComboOption<ImageFormatChoice>(ImageFormatChoice.Jpeg, "JPEG"),
            new ComboOption<ImageFormatChoice>(ImageFormatChoice.WebP, "WebP"),
            new ComboOption<ImageFormatChoice>(ImageFormatChoice.Avif, "AVIF")
        };
        NotificationModeCombo.ItemsSource = new[]
        {
            new ComboOption<NotificationTriggerMode>(NotificationTriggerMode.Off, "Off"),
            new ComboOption<NotificationTriggerMode>(NotificationTriggerMode.EveryScreenshots, "Every N screenshots"),
            new ComboOption<NotificationTriggerMode>(NotificationTriggerMode.TimedReminder, "Timed reminder")
        };

        TargetModeCombo.SelectedIndex = 0;
        BackendCombo.SelectedIndex = 0;
        FormatCombo.SelectedIndex = 0;
        NotificationModeCombo.SelectedIndex = 1;
        WaylandNotice.IsVisible = PlatformInfo.IsWayland;
    }

    private void MinimizeButton_Click(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        WindowState = WindowState.Minimized;
    }

    private void MaximizeButton_Click(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;

        if (WindowState == WindowState.Maximized)
        {
            _restoringWindowBounds = true;
            WindowState = WindowState.Normal;
            Dispatcher.UIThread.Post(() =>
            {
                Width = _normalWidth;
                Height = _normalHeight;
                Position = _normalPosition;
                _restoringWindowBounds = false;
            });
            return;
        }

        RememberNormalBounds();
        WindowState = WindowState.Maximized;
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        _ = ShutdownAsync();
    }

    private void RememberNormalBounds()
    {
        if (WindowState != WindowState.Normal)
            return;

        if (double.IsFinite(Width) && Width >= MinWidth)
            _normalWidth = Width;
        if (double.IsFinite(Height) && Height >= MinHeight)
            _normalHeight = Height;
        _normalPosition = Position;
    }

    private async void OpenSettings_Click(object? sender, RoutedEventArgs e)
    {
        if (_settingsWindow is not null)
            return;

        var current = ReadSettings(false, out _);
        await SaveSettingsAsync(current);

        var settingsWindow = new SettingsWindow(current);
        _settingsWindow = settingsWindow;
        settingsWindow.SettingsChanged += updated =>
        {
            _settings = updated;
            ApplyUiScale(updated.UiScale, true);
        };
        settingsWindow.Closed += (_, _) => _settingsWindow = null;
        settingsWindow.Show(this);
    }

    private void RefreshWindows_Click(object? sender, RoutedEventArgs e) => RefreshWindowList(false);

    private void RefreshWindowList(bool updatePrefix)
    {
        try
        {
            var windows = WindowFinder.GetOpenWindows();
            _ignoreWindowSelection = true;
            OpenWindowCombo.ItemsSource = windows;
            OpenWindowCombo.SelectedItem = null;
            _ignoreWindowSelection = false;

            if (windows.Count == 0)
            {
                TargetHelpText.Text = PlatformInfo.IsWayland
                    ? "No X11/XWayland windows were found. Use Wayland window stream for native Wayland applications."
                    : "No capturable windows were found.";
                return;
            }

            if (updatePrefix)
                ApplySelectedWindow();
        }
        catch (Exception ex)
        {
            _ignoreWindowSelection = false;
            TargetHelpText.Text = $"Window list unavailable: {ex.Message}";
        }
    }

    private void OpenWindowCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_ignoreWindowSelection || OpenWindowCombo.SelectedItem is not WindowInfo selected)
            return;

        ApplyWindow(selected, true);
    }

    private void UseWindow_Click(object? sender, RoutedEventArgs e)
    {
        if (OpenWindowCombo.SelectedItem is WindowInfo selected)
            ApplyWindow(selected, true);
    }

    private void ApplyWindow(WindowInfo window, bool updatePrefix)
    {
        var mode = SelectedValue(TargetModeCombo, TargetMode.ProcessExe);
        TargetValueBox.Text = mode switch
        {
            TargetMode.ProcessExe when !string.IsNullOrWhiteSpace(window.ProcessName) => window.ProcessName,
            TargetMode.WindowTitle => window.Title,
            TargetMode.WindowHandle when window.HasNativeId => window.NativeId,
            TargetMode.WindowHandle => $"0x{window.Handle.ToInt64():X}",
            _ => window.Title
        };

        _selectedNativeWindowId = window.NativeId;
        _selectedNativeTargetValue = TargetValueBox.Text?.Trim() ?? string.Empty;

        if (updatePrefix)
        {
            var raw = !string.IsNullOrWhiteSpace(window.ProcessName) ? window.ProcessName : window.Title;
            PrefixBox.Text = OutputPathBuilder.SafeFileName(raw);
        }

        _ = SaveCurrentSettingsAsync();
        UpdateTargetHelp();
    }

    private void ApplySelectedWindow()
    {
        if (OpenWindowCombo.SelectedItem is WindowInfo selected)
            ApplyWindow(selected, true);
    }

    private void TargetModeCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        UpdateTargetHelp();
        if (!_loading && OpenWindowCombo.SelectedItem is WindowInfo selected)
            ApplyWindow(selected, false);
    }

    private void BackendCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        UpdateBackendHelp();
        if (!_loading)
            _ = SaveCurrentSettingsAsync();
    }

    private void FormatCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_loading)
            _ = SaveCurrentSettingsAsync();
    }

    private void UpdateTargetHelp()
    {
        var mode = SelectedValue(TargetModeCombo, TargetMode.ProcessExe);
        TargetHelpText.Text = mode switch
        {
            TargetMode.ProcessExe => "Matches a running application's process name. Selecting an open window fills this automatically.",
            TargetMode.WindowTitle => "Matches the first window whose title contains this text.",
            TargetMode.WindowHandle when PlatformInfo.IsWindows => "Targets a native HWND directly. Hex values such as 0x001A0F32 are accepted.",
            TargetMode.WindowHandle => "Targets an X11 window ID directly. Hex values are accepted.",
            _ => string.Empty
        };
    }

    private void UpdateBackendHelp()
    {
        var backend = SelectedValue(BackendCombo, CaptureBackend.Auto);
        BackendHelpText.Text = backend switch
        {
            CaptureBackend.Auto when PlatformInfo.IsWindows => "Recommended. Tries Windows Graphics Capture first, then compatible fallbacks.",
            CaptureBackend.Auto when PlatformInfo.IsWayland => PlatformInfo.HasX11
                ? "Uses X11/XWayland tools for a selected XWayland window; native Wayland windows use a persistent ScreenCast/PipeWire stream."
                : "Native Wayland capture uses a ScreenCast/PipeWire window stream. You choose the window once when the stream starts.",
            CaptureBackend.Auto => "Tries direct X11 capture first, then ImageMagick and xwd fallbacks when available.",
            CaptureBackend.WindowsGraphicsCapture => "Modern Windows capture path for most windowed and borderless applications.",
            CaptureBackend.DxgiDesktopDuplication => "Captures from the desktop output and crops to the target window.",
            CaptureBackend.PrintWindow => "Traditional Win32 window capture. Useful for normal desktop applications.",
            CaptureBackend.ScreenCopy => "Copies the visible target area from the desktop. The window must be visible.",
            CaptureBackend.PortableWindow when PlatformInfo.IsLinux => "Direct X11/XWayland capture through the built-in capture library.",
            CaptureBackend.PortableWindow => "Cross-platform window capture backend.",
            CaptureBackend.WaylandPortal => "Opens the desktop screen-share chooser once, then keeps the selected window available through PipeWire for scheduled screenshots.",
            CaptureBackend.LinuxImageMagickX11 => "Captures the selected X11 window by ID with ImageMagick import. The window should be visible.",
            CaptureBackend.LinuxXwdImageMagick => "Uses xwd for the selected X11 window, then ImageMagick to convert the frame.",
            CaptureBackend.LinuxGnomeScreenshot when PlatformInfo.IsWayland => "Captures GNOME's currently active window. Native Wayland does not expose arbitrary window handles.",
            CaptureBackend.LinuxGnomeScreenshot => "Focuses the selected X11 window, then captures the active window with GNOME Screenshot.",
            CaptureBackend.LinuxXfceScreenshot => "Focuses the selected X11 window, then captures it with XFCE Screenshooter.",
            CaptureBackend.LinuxMaim => "Captures the selected X11/XWayland window directly by its window ID using maim.",
            CaptureBackend.LinuxScrot => "Captures the selected X11/XWayland window directly by its window ID using scrot.",
            CaptureBackend.LinuxSwayGrim => "Reads the focused Sway window geometry and captures that rectangle with grim.",
            CaptureBackend.LinuxHyprlandGrim => "Reads Hyprland's active window geometry and captures it with grim.",
            CaptureBackend.LinuxHyprshot => "Captures Hyprland's active window with Hyprshot.",
            CaptureBackend.LinuxGrimblast => "Captures Hyprland's active window with grimblast.",
            _ => string.Empty
        };

        UpdateTargetAvailability(backend);
    }

    private void UpdateTargetAvailability(CaptureBackend backend)
    {
        var needsTarget = CaptureBackendInfo.RequiresWindowTarget(backend);
        var optionalTarget = CaptureBackendInfo.HasOptionalWindowTarget(backend);
        var targetEnabled = needsTarget || optionalTarget;

        TargetModeCombo.IsEnabled = targetEnabled;
        TargetValueBox.IsEnabled = targetEnabled;
        OpenWindowCombo.IsEnabled = targetEnabled;

        if (optionalTarget)
        {
            TargetHelpText.Text = "Optional on Wayland: choose an XWayland window for direct capture, or leave this blank to choose a native Wayland window through ScreenCast.";
            return;
        }

        if (needsTarget)
        {
            UpdateTargetHelp();
            return;
        }

        TargetHelpText.Text = backend switch
        {
            CaptureBackend.WaylandPortal =>
                "Your desktop decides what gets captured for this method.",
            CaptureBackend.Auto when PlatformInfo.IsWayland =>
                "Auto uses ScreenCast/PipeWire for native Wayland windows, so the desktop asks you to choose the window once.",
            _ => "This method captures the compositor's current active or focused window."
        };
    }

    private async void BrowseFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (!StorageProvider.CanPickFolder)
            return;

        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose screenshot folder",
            AllowMultiple = false
        });

        var folder = folders.FirstOrDefault();
        if (folder is null)
            return;

        OutputFolderBox.Text = folder.Path.LocalPath;
        await SaveCurrentSettingsAsync();
    }

    private async void OpenFolder_Click(object? sender, RoutedEventArgs e)
    {
        var path = OutputFolderBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            Directory.CreateDirectory(path);
            var folder = await StorageProvider.TryGetFolderFromPathAsync(path);
            if (folder is not null)
            {
                await Launcher.LaunchFileAsync(folder);
                return;
            }
        }
        catch (Exception ex)
        {
            SetStatus($"Could not open folder: {ex.Message}", _isRunning, true);
        }
    }

    private async void TestNotification_Click(object? sender, RoutedEventArgs e)
    {
        var settings = ReadSettings(false, out _);
        await SaveSettingsAsync(settings);

        if (settings.NotificationMode == NotificationTriggerMode.TimedReminder)
            ToastManager.ShowReminder(settings.ToastScale, settings.ToastDurationSeconds, _sessionTimer.IsRunning ? _sessionTimer.Elapsed : TimeSpan.Zero);
        else
            ToastManager.ShowTest(settings.ToastScale, settings.ToastDurationSeconds);

        if (!settings.NotificationSoundEnabled)
        {
            SetStatus("Notification preview shown — sound muted", _isRunning);
            return;
        }

        var playback = NotificationSoundService.Play(settings.NotificationSoundPath, settings.NotificationSoundVolume);
        SetStatus(playback.Success ? "Notification preview shown" : $"Sound error: {playback.Error}", _isRunning, !playback.Success);
    }

    private async void NotificationModeCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        UpdateNotificationUi();
        if (_loading)
            return;

        var settings = ReadSettings(false, out _);
        await SaveSettingsAsync(settings);
        if (_isRunning)
            RestartReminder(settings);
    }

    private void UpdateNotificationUi()
    {
        var mode = SelectedValue(NotificationModeCombo, NotificationTriggerMode.Off);
        ScreenshotNotificationPanel.IsVisible = mode == NotificationTriggerMode.EveryScreenshots;
        TimedNotificationPanel.IsVisible = mode == NotificationTriggerMode.TimedReminder;
        NotificationOffPanel.IsVisible = mode == NotificationTriggerMode.Off;
        NotificationModeHelpText.Text = mode switch
        {
            NotificationTriggerMode.EveryScreenshots => "Toast + sound after the chosen number of successful screenshots.",
            NotificationTriggerMode.TimedReminder => "Toast + sound on a timer that is independent from screenshot timing.",
            _ => "Automatic notifications are disabled."
        };
    }

    private async void NotificationSettings_LostFocus(object? sender, RoutedEventArgs e)
    {
        var settings = ReadSettings(false, out _);
        await SaveSettingsAsync(settings);
        if (_isRunning && ReferenceEquals(sender, ReminderIntervalBox)
            && settings.NotificationMode == NotificationTriggerMode.TimedReminder)
        {
            RestartReminder(settings);
        }
    }

    private async void Settings_LostFocus(object? sender, RoutedEventArgs e)
    {
        if (!_loading)
            await SaveCurrentSettingsAsync();
    }

    private void ApplyUiScale(double scale, bool resizeWindow)
    {
        scale = NormalizeUiScale(scale);
        UiScaleHost.LayoutTransform = new ScaleTransform
        {
            ScaleX = scale,
            ScaleY = scale
        };

        if (!resizeWindow || WindowState != WindowState.Normal)
            return;

        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null)
            return;

        var maxWidth = screen.WorkingArea.Width / screen.Scaling - 24;
        var maxHeight = screen.WorkingArea.Height / screen.Scaling - 24;
        Width = Math.Min(BaseWindowWidth * scale, maxWidth);
        Height = Math.Min(BaseWindowHeight * scale, maxHeight);
        RememberNormalBounds();
    }

    private static double NormalizeUiScale(double scale) =>
        Math.Clamp(double.IsFinite(scale) && scale > 0 ? scale : 1, MinUiScale, MaxUiScale);

    private async Task SaveCurrentSettingsAsync()
    {
        var settings = ReadSettings(false, out _);
        await SaveSettingsAsync(settings);
    }

    private async Task SaveSettingsAsync(CaptureSettings settings)
    {
        _settings = settings;
        try
        {
            await SettingsStore.SaveAsync(settings);
        }
        catch (Exception ex)
        {
            SetStatus($"Settings could not be saved: {ex.Message}", _isRunning, true);
        }
    }

    private static T SelectedValue<T>(ComboBox combo, T fallback)
    {
        return combo.SelectedItem is ComboOption<T> option ? option.Value : fallback;
    }

    private static bool TryReadDouble(string? text, out double value)
    {
        return double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value)
            || double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
