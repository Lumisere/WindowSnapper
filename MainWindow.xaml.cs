using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Microsoft.Win32;
using WindowSnapper.Models;
using WindowSnapper.Services;

namespace WindowSnapper;

public partial class MainWindow : Window
{
    private sealed record ComboOption<T>(T Value, string Label);

    private const double BaseToastWidth = 344;
    private const double BaseToastHeight = 82;
    private const double MinToastScale = 0.8;
    private const double MaxToastScale = 2.0;

    private readonly SemaphoreSlim _captureLock = new(1, 1);
    private readonly Stopwatch _sessionTimer = new();
    private CancellationTokenSource? _captureCts;
    private CancellationTokenSource? _reminderCts;
    private CaptureSettings _settings = new();
    private string _soundPath = string.Empty;
    private bool _isRunning;
    private long _successfulCaptureCount;
    private bool _ignoreWindowSelection;
    private bool _updatingNotificationControls;

    public MainWindow()
    {
        InitializeComponent();

        MaxHeight = Math.Max(MinHeight, SystemParameters.WorkArea.Height - 16);
        Height = Math.Min(Height, MaxHeight);

        TargetModeCombo.ItemsSource = new[]
        {
            new ComboOption<TargetMode>(TargetMode.ProcessExe, "Process / EXE name"),
            new ComboOption<TargetMode>(TargetMode.WindowTitle, "Window title"),
            new ComboOption<TargetMode>(TargetMode.WindowHandle, "Window handle (HWND)")
        };

        BackendCombo.ItemsSource = new[]
        {
            new ComboOption<CaptureBackend>(CaptureBackend.Auto, "Auto"),
            new ComboOption<CaptureBackend>(CaptureBackend.WindowsGraphicsCapture, "Windows Graphics Capture"),
            new ComboOption<CaptureBackend>(CaptureBackend.DxgiDesktopDuplication, "DXGI Desktop Duplication"),
            new ComboOption<CaptureBackend>(CaptureBackend.PrintWindow, "PrintWindow"),
            new ComboOption<CaptureBackend>(CaptureBackend.ScreenCopy, "Screen Copy")
        };

        FormatCombo.ItemsSource = new[]
        {
            new ComboOption<ImageFormatChoice>(ImageFormatChoice.Png, "PNG"),
            new ComboOption<ImageFormatChoice>(ImageFormatChoice.Jpeg, "JPEG")
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

        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var preference = NativeMethods.DWMWCP_ROUND;
            var result = NativeMethods.DwmSetWindowAttribute(
                hwnd,
                NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE,
                ref preference,
                sizeof(int));

            if (result != 0)
                Debug.WriteLine($"DwmSetWindowAttribute failed with HRESULT 0x{result:X8}.");
        }
        catch
        {
            // The WPF border still provides rounded corners on older Windows builds.
        }
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _settings = await SettingsStore.LoadAsync();
        ApplySettings(_settings);
        RefreshWindowList(false);
        UpdateTargetStatus();
        UpdateBackendHelp();
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _captureCts?.Cancel();
        _reminderCts?.Cancel();
        _sessionTimer.Stop();
        SettingsStore.Save(ReadSettings(false, out _));
    }

    private async void TestToast_Click(object sender, RoutedEventArgs e)
    {
        var settings = ReadSettings(false, out _);
        await SaveSettingsAsync(settings);

        ToastManager.ShowTest(settings.ToastScale);
        if (!settings.NotificationSoundEnabled)
        {
            SetCaptureStatus("Notification preview shown · sound muted", _isRunning);
            return;
        }

        var result = NotificationSoundService.Play(settings.NotificationSoundPath);
        SetCaptureStatus(
            result.Success ? "Notification preview shown" : $"Notification sound error: {result.Error}",
            _isRunning,
            !result.Success);
    }

    private async void NotificationModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateNotificationModeUi();

        if (_updatingNotificationControls || !IsLoaded)
            return;

        var settings = ReadSettings(false, out _);
        await SaveSettingsAsync(settings);

        if (_isRunning)
            RestartReminder(settings);
    }

    private void UpdateNotificationModeUi()
    {
        if (NotificationModeCombo is null || ScreenshotNotificationPanel is null || TimedNotificationPanel is null)
            return;

        var mode = SelectedValue(NotificationModeCombo, NotificationTriggerMode.Off);
        ScreenshotNotificationPanel.Visibility = mode == NotificationTriggerMode.EveryScreenshots
            ? Visibility.Visible
            : Visibility.Collapsed;
        TimedNotificationPanel.Visibility = mode == NotificationTriggerMode.TimedReminder
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (NotificationModeHelpText is null)
            return;

        NotificationModeHelpText.Text = mode switch
        {
            NotificationTriggerMode.EveryScreenshots => "Show a notification after a chosen number of screenshots.",
            NotificationTriggerMode.TimedReminder => "Show a notification on an independent timer.",
            _ => "Automatic notifications are disabled. Preview controls still work."
        };
    }

    private async void NotificationSettings_LostFocus(object sender, RoutedEventArgs e)
    {
        var settings = ReadSettings(false, out _);
        await SaveSettingsAsync(settings);

        if (_isRunning && ReferenceEquals(sender, SoundIntervalBox)
            && settings.NotificationMode == NotificationTriggerMode.TimedReminder)
        {
            RestartReminder(settings);
        }
    }

    private async void TestReminder_Click(object sender, RoutedEventArgs e)
    {
        var settings = ReadSettings(false, out _);
        await SaveSettingsAsync(settings);

        ToastManager.ShowReminder(settings.ToastScale, _sessionTimer.IsRunning ? _sessionTimer.Elapsed : TimeSpan.Zero);
        if (!settings.NotificationSoundEnabled)
        {
            SetCaptureStatus("Reminder preview shown · sound muted", _isRunning);
            return;
        }

        var result = NotificationSoundService.Play(settings.NotificationSoundPath);
        SetCaptureStatus(
            result.Success ? "Reminder preview shown" : $"Reminder sound error: {result.Error}",
            _isRunning,
            !result.Success);
    }

    private async void ChooseSound_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose notification sound",
            Filter = "Supported audio|*.mp3;*.wav;*.wma;*.m4a;*.aac;*.aif;*.aiff;*.flac|MP3 audio (*.mp3)|*.mp3|Wave audio (*.wav)|*.wav|Windows Media Audio (*.wma)|*.wma|M4A / AAC (*.m4a;*.aac)|*.m4a;*.aac|AIFF audio (*.aif;*.aiff)|*.aif;*.aiff|FLAC audio (*.flac)|*.flac|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog() != true)
            return;

        _soundPath = dialog.FileName;
        UpdateSoundPathDisplay();
        await SaveSettingsAsync(ReadSettings(false, out _));
        SetCaptureStatus($"Sound: {Path.GetFileName(_soundPath)}", _isRunning);
    }

    private async void UseDefaultSound_Click(object sender, RoutedEventArgs e)
    {
        _soundPath = string.Empty;
        UpdateSoundPathDisplay();
        await SaveSettingsAsync(ReadSettings(false, out _));
        SetCaptureStatus("Using notif.mp3", _isRunning);
    }

    private async void NotificationSoundToggle_Click(object sender, RoutedEventArgs e)
    {
        UpdateSoundEnabledUi();

        if (_updatingNotificationControls || !IsLoaded)
            return;

        var settings = ReadSettings(false, out _);
        await SaveSettingsAsync(settings);

        if (!settings.NotificationSoundEnabled)
            NotificationSoundService.Stop();

        SetCaptureStatus(settings.NotificationSoundEnabled ? "Notification sound enabled" : "Notification sound muted", _isRunning);
    }

    private async void TestSound_Click(object sender, RoutedEventArgs e)
    {
        var settings = ReadSettings(false, out _);
        await SaveSettingsAsync(settings);

        if (!settings.NotificationSoundEnabled)
        {
            SetCaptureStatus("Notification sound is muted", _isRunning);
            return;
        }

        var result = NotificationSoundService.Play(settings.NotificationSoundPath);
        var message = result.Success
            ? result.UsedFallback
                ? $"Selected sound unavailable — played {result.DisplayName}"
                : $"Played {result.DisplayName}"
            : $"Could not play notification sound: {result.Error}";

        SetCaptureStatus(message, _isRunning, !result.Success);
    }

    private void UpdateSoundPathDisplay()
    {
        if (SoundPathBox is null)
            return;

        var useDefault = string.IsNullOrWhiteSpace(_soundPath);
        SoundPathBox.Text = useDefault ? "notif.mp3 (default)" : _soundPath;
        SoundPathBox.ToolTip = useDefault ? "Bundled notification sound" : _soundPath;
    }

    private void UpdateSoundEnabledUi()
    {
        if (NotificationSoundToggle is null)
            return;

        var enabled = NotificationSoundToggle.IsChecked == true;
        if (TestSoundButton is not null)
            TestSoundButton.IsEnabled = enabled;

        if (NotificationSoundHelpText is not null)
        {
            NotificationSoundHelpText.Text = enabled
                ? "Played together with the toast whenever the selected trigger fires."
                : "Muted. Notifications still show their toast without playing audio.";
        }
    }

    private void RefreshWindows_Click(object sender, RoutedEventArgs e)
    {
        RefreshWindowList(true);
    }

    private void RefreshWindowList(bool showStatus)
    {
        var windows = WindowFinder.GetOpenWindows()
            .Where(window => window.ProcessId != Environment.ProcessId)
            .ToList();

        var selectedHandle = (OpenWindowsCombo.SelectedItem as WindowInfo)?.Handle;

        _ignoreWindowSelection = true;
        try
        {
            OpenWindowsCombo.ItemsSource = windows;

            if (selectedHandle.HasValue)
                OpenWindowsCombo.SelectedItem = windows.FirstOrDefault(window => window.Handle == selectedHandle.Value);

            if (OpenWindowsCombo.SelectedItem is null && windows.Count > 0)
                OpenWindowsCombo.SelectedIndex = 0;
        }
        finally
        {
            _ignoreWindowSelection = false;
        }

        if (showStatus)
            SetCaptureStatus($"Found {windows.Count} open windows", _isRunning);
    }

    private void UseSelectedWindow_Click(object sender, RoutedEventArgs e)
    {
        if (OpenWindowsCombo.SelectedItem is not WindowInfo window)
            return;

        var mode = SelectedValue(TargetModeCombo, TargetMode.ProcessExe);
        TargetValueBox.Text = mode switch
        {
            TargetMode.ProcessExe => $"{window.ProcessName}.exe",
            TargetMode.WindowTitle => window.Title,
            TargetMode.WindowHandle => $"0x{window.Handle.ToInt64():X}",
            _ => window.ProcessName
        };

        PrefixBox.Text = FileNamePrefixFor(window);
        UpdateTargetStatus();
    }

    private void OpenWindowsCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_ignoreWindowSelection)
            return;

        if (OpenWindowsCombo.SelectedItem is WindowInfo window)
            PrefixBox.Text = FileNamePrefixFor(window);
    }

    private static string FileNamePrefixFor(WindowInfo window)
    {
        return string.IsNullOrWhiteSpace(window.ProcessName)
            ? "capture"
            : window.ProcessName.Trim();
    }

    private void TargetModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TargetValueLabel is null)
            return;

        TargetValueLabel.Text = SelectedValue(TargetModeCombo, TargetMode.ProcessExe) switch
        {
            TargetMode.ProcessExe => "Process / EXE name",
            TargetMode.WindowTitle => "Window title contains",
            TargetMode.WindowHandle => "Window handle (decimal or 0x...)",
            _ => "Target"
        };

        UpdateTargetStatus();
    }

    private void TargetValueBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateTargetStatus();
    }

    private void UpdateTargetStatus()
    {
        if (TargetStatusText is null || TargetModeCombo is null || TargetValueBox is null)
            return;

        var value = TargetValueBox.Text.Trim();
        if (value.Length == 0)
        {
            SetTargetStatus("No target selected", "MutedBrush");
            return;
        }

        var hwnd = WindowFinder.Resolve(SelectedValue(TargetModeCombo, TargetMode.ProcessExe), value);
        if (hwnd == IntPtr.Zero)
        {
            SetTargetStatus("Target not found", "DangerBrush");
            return;
        }

        var window = WindowFinder.GetOpenWindows().FirstOrDefault(item => item.Handle == hwnd);
        var text = window is null
            ? $"Ready — HWND 0x{hwnd.ToInt64():X}"
            : $"Ready — {window.ProcessName}.exe — {window.Title}";

        SetTargetStatus(text, "TextBrush");
    }

    private void SetTargetStatus(string text, string textBrush)
    {
        TargetStatusText.Text = text;
        TargetStatusText.Foreground = (System.Windows.Media.Brush)FindResource(textBrush);
    }

    private void BackendCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateBackendHelp();
    }

    private void UpdateBackendHelp()
    {
        if (BackendHelpText is null)
            return;

        BackendHelpText.Text = SelectedValue(BackendCombo, CaptureBackend.Auto) switch
        {
            CaptureBackend.Auto => "Tries Windows Graphics Capture first, then falls back to PrintWindow, DXGI, and Screen Copy.",
            CaptureBackend.WindowsGraphicsCapture => "Best choice for most normal and hardware-accelerated windows.",
            CaptureBackend.DxgiDesktopDuplication => "Captures the visible desktop and crops it to the target window. Useful for borderless and fullscreen content.",
            CaptureBackend.PrintWindow => "Asks the target window to render itself. Some GPU surfaces can come back black.",
            CaptureBackend.ScreenCopy => "Copies visible screen pixels. The target must be visible and unobstructed.",
            _ => string.Empty
        };
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choose screenshot folder",
            InitialDirectory = Directory.Exists(FolderBox.Text)
                ? FolderBox.Text
                : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)
        };

        if (dialog.ShowDialog() == true)
            FolderBox.Text = dialog.FolderName;
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var folder = FolderBox.Text.Trim();
        if (folder.Length == 0)
        {
            SetCaptureStatus("Choose an output folder first", _isRunning, true);
            return;
        }

        Directory.CreateDirectory(folder);
        Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
    }

    private void Minimize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeRestore_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        if (MaximizeButton is not null)
            MaximizeButton.Content = WindowState == WindowState.Maximized ? "❐" : "□";

        if (MainWindowFrame is not null)
            MainWindowFrame.CornerRadius = WindowState == WindowState.Maximized
                ? new CornerRadius(0)
                : new CornerRadius(12);
    }
}
