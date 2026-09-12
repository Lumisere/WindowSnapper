using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using WindowSnapper.Models;
using WindowSnapper.Services;

namespace WindowSnapper;

public sealed partial class SettingsWindow : Window
{
    private const double BaseToastWidth = 344;
    private const double BaseToastHeight = 82;
    private const double BaseWindowWidth = 900;
    private const double BaseWindowHeight = 720;
    private const double BaseMinWindowWidth = 720;
    private const double BaseMinWindowHeight = 600;

    private readonly CaptureSettings _settings;
    private bool _loading = true;
    private bool _navigationExpanded = true;
    private string _activeSettingsPage = "General";
    private int _pageAnimationGeneration;
    private CancellationTokenSource? _saveDebounceCts;

    public event Action<CaptureSettings>? SettingsChanged;

    public SettingsWindow() : this(new CaptureSettings())
    {
    }

    public SettingsWindow(CaptureSettings settings)
    {
        InitializeComponent();
        _settings = settings;

        InterfaceScaleSlider.Value = NormalizeUiScale(settings.UiScale);
        ToastScaleSlider.Value = NormalizeToastScale(settings.ToastScale);
        ToastDurationSlider.Value = NormalizeToastDuration(settings.ToastDurationSeconds);
        NotificationVolumeSlider.Value = NormalizeNotificationVolume(settings.NotificationSoundVolume) * 100.0;
        SoundToggle.IsChecked = settings.NotificationSoundEnabled;
        ClipboardToggle.IsChecked = settings.CopyLatestToClipboard;
        HdrNormalizeToggle.IsChecked = settings.NormalizeHdrCaptures;
        CaptureCursorToggle.IsChecked = settings.CaptureCursor;
        ExclusiveFullscreenToggle.IsChecked = settings.ExclusiveFullscreenCompatibility;
        ExclusiveFullscreenPanel.IsVisible = PlatformInfo.IsWindows;
        ExclusiveFullscreenDivider.IsVisible = PlatformInfo.IsWindows;
        WatermarkToggle.IsChecked = settings.CaptureWatermarkEnabled;
        HotkeysToggle.IsChecked = settings.HotkeysEnabled;
        CaptureHotkeyBox.Text = settings.CaptureNowHotkey;
        ToggleHotkeyBox.Text = settings.ToggleCaptureHotkey;

        UpdateInterfaceScaleReadout();
        UpdateToastScaleReadout();
        UpdateToastDurationReadout();
        UpdateNotificationVolumeReadout();
        UpdateSoundDisplay();
        UpdateSoundUi();
        ApplyWindowScale(NormalizeUiScale(settings.UiScale));
        _loading = false;
    }


    private void ToggleNavigation_Click(object? sender, RoutedEventArgs e)
    {
        _navigationExpanded = !_navigationExpanded;
        SettingsNavigation.IsHitTestVisible = _navigationExpanded;
        SettingsNavigation.Opacity = _navigationExpanded ? 1 : 0;
        SettingsNavigation.Width = _navigationExpanded ? 190 : 0;
    }

    private async void ShowGeneral_Click(object? sender, RoutedEventArgs e) => await ShowSettingsPageAsync("General");
    private async void ShowCapture_Click(object? sender, RoutedEventArgs e) => await ShowSettingsPageAsync("Capture");
    private async void ShowHotkeys_Click(object? sender, RoutedEventArgs e) => await ShowSettingsPageAsync("Hotkeys");
    private async void ShowNotifications_Click(object? sender, RoutedEventArgs e) => await ShowSettingsPageAsync("Notifications");

    private async Task ShowSettingsPageAsync(string page)
    {
        if (page == _activeSettingsPage)
            return;

        var generation = ++_pageAnimationGeneration;
        var oldPage = SettingsPageFor(_activeSettingsPage);
        var newPage = SettingsPageFor(page);

        SetSelectedSettingsCategory(page);
        SettingsPageTitleText.Text = page switch
        {
            "Capture" => "Capture & verification",
            "Hotkeys" => "Hotkeys",
            "Notifications" => "Notifications",
            _ => "General"
        };

        oldPage.Opacity = 0;
        await Task.Delay(85);
        if (generation != _pageAnimationGeneration)
            return;

        oldPage.IsVisible = false;
        newPage.Opacity = 0;
        newPage.IsVisible = true;
        _activeSettingsPage = page;

        // Avalonia needs one frame to notice the opacity changed. Yes, really.
        await Task.Delay(16);
        if (generation != _pageAnimationGeneration)
            return;

        newPage.Opacity = 1;
    }

    private ScrollViewer SettingsPageFor(string page) => page switch
    {
        "Capture" => CapturePage,
        "Hotkeys" => HotkeysPage,
        "Notifications" => NotificationsPage,
        _ => GeneralPage
    };

    private void SetSelectedSettingsCategory(string page)
    {
        GeneralNavButton.Classes.Set("selected", page == "General");
        CaptureNavButton.Classes.Set("selected", page == "Capture");
        HotkeysNavButton.Classes.Set("selected", page == "Hotkeys");
        NotificationsNavButton.Classes.Set("selected", page == "Notifications");
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        FitWindowToWorkingArea();
    }

    private async void Close_Click(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        await SaveAndNotifyAsync();
        Close();
    }

    private void InterfaceScaleSlider_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        UpdateInterfaceScaleReadout();
        if (_loading)
            return;

        _settings.UiScale = NormalizeUiScale(InterfaceScaleSlider.Value);
        ApplyWindowScale(_settings.UiScale);
        SaveAndNotify();
    }

    private void ToastScaleSlider_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        UpdateToastScaleReadout();
        if (_loading)
            return;

        var scale = NormalizeToastScale(ToastScaleSlider.Value);
        _settings.ToastScale = scale;
        _settings.ToastWidth = BaseToastWidth * scale;
        _settings.ToastHeight = BaseToastHeight * scale;
        SaveAndNotify();
    }

    private void ToastDurationSlider_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        UpdateToastDurationReadout();
        if (_loading)
            return;

        _settings.ToastDurationSeconds = NormalizeToastDuration(ToastDurationSlider.Value);
        SaveAndNotify();
    }

    private void NotificationVolumeSlider_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        UpdateNotificationVolumeReadout();
        if (_loading)
            return;

        _settings.NotificationSoundVolume = NormalizeNotificationVolume(NotificationVolumeSlider.Value / 100.0);
        SaveAndNotify();
    }

    private async void ChooseSound_Click(object? sender, RoutedEventArgs e)
    {
        if (!StorageProvider.CanOpen)
            return;

        var audioType = new FilePickerFileType("Supported audio")
        {
            Patterns = new[] { "*.mp3", "*.wav", "*.wma", "*.m4a", "*.aac", "*.aif", "*.aiff", "*.flac" },
            MimeTypes = new[] { "audio/*" }
        };

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose notification sound",
            AllowMultiple = false,
            FileTypeFilter = new[] { audioType, FilePickerFileTypes.All }
        });

        var file = files.FirstOrDefault();
        if (file is null)
            return;

        _settings.NotificationSoundPath = file.Path.LocalPath;
        UpdateSoundDisplay();
        await SaveAndNotifyAsync();
        SettingsStatusText.Text = $"Sound: {Path.GetFileName(_settings.NotificationSoundPath)}";
    }

    private async void DefaultSound_Click(object? sender, RoutedEventArgs e)
    {
        _settings.NotificationSoundPath = string.Empty;
        UpdateSoundDisplay();
        await SaveAndNotifyAsync();
        SettingsStatusText.Text = "Using notif.mp3";
    }

    private async void SoundToggle_Click(object? sender, RoutedEventArgs e)
    {
        _settings.NotificationSoundEnabled = SoundToggle.IsChecked == true;
        UpdateSoundUi();

        if (!_settings.NotificationSoundEnabled)
            NotificationSoundService.Stop();

        await SaveAndNotifyAsync();
        SettingsStatusText.Text = _settings.NotificationSoundEnabled ? "Notification sound enabled" : "Notification sound muted";
    }

    private void TestSound_Click(object? sender, RoutedEventArgs e)
    {
        if (!_settings.NotificationSoundEnabled)
        {
            SettingsStatusText.Text = "Notification sound is muted";
            return;
        }

        var result = NotificationSoundService.Play(_settings.NotificationSoundPath, _settings.NotificationSoundVolume);
        SettingsStatusText.Text = result.Success ? $"Played {result.DisplayName}" : $"Sound error: {result.Error}";
    }

    private async void HdrNormalizeToggle_Click(object? sender, RoutedEventArgs e)
    {
        _settings.NormalizeHdrCaptures = HdrNormalizeToggle.IsChecked == true;
        await SaveAndNotifyAsync();
        SettingsStatusText.Text = _settings.NormalizeHdrCaptures
            ? "HDR normalization enabled"
            : "HDR normalization disabled";
    }

    private async void CaptureCursorToggle_Click(object? sender, RoutedEventArgs e)
    {
        _settings.CaptureCursor = CaptureCursorToggle.IsChecked == true;
        await SaveAndNotifyAsync();
        SettingsStatusText.Text = _settings.CaptureCursor
            ? "Cursor capture enabled"
            : "Cursor will be hidden from captures";
    }

    private async void ExclusiveFullscreenToggle_Click(object? sender, RoutedEventArgs e)
    {
        _settings.ExclusiveFullscreenCompatibility = ExclusiveFullscreenToggle.IsChecked == true;
        await SaveAndNotifyAsync();
        SettingsStatusText.Text = _settings.ExclusiveFullscreenCompatibility
            ? "Exclusive fullscreen compatibility enabled"
            : "Exclusive fullscreen compatibility disabled";
    }

    private async void ClipboardToggle_Click(object? sender, RoutedEventArgs e)
    {
        _settings.CopyLatestToClipboard = ClipboardToggle.IsChecked == true;
        await SaveAndNotifyAsync();
        SettingsStatusText.Text = _settings.CopyLatestToClipboard
            ? "New captures will be copied to the clipboard"
            : "Clipboard copy disabled";
    }

    private async void WatermarkToggle_Click(object? sender, RoutedEventArgs e)
    {
        _settings.CaptureWatermarkEnabled = WatermarkToggle.IsChecked == true;
        await SaveAndNotifyAsync();
        SettingsStatusText.Text = _settings.CaptureWatermarkEnabled
            ? "Toast-style bottom-left watermark enabled"
            : "Capture watermark disabled";
    }

    private void VerifyWatermark_Click(object? sender, RoutedEventArgs e)
    {
        var result = CaptureVerificationService.Verify(
            _settings,
            WatermarkVerifyIdBox.Text,
            WatermarkVerifyCodeBox.Text);

        SettingsStatusText.Text = result.IsValid
            ? "Verified"
            : "Altered / Not Verified";
    }

    private async void VerifyImage_Click(object? sender, RoutedEventArgs e)
    {
        if (!StorageProvider.CanOpen)
        {
            SettingsStatusText.Text = "File picker is unavailable";
            return;
        }

        var imageType = new FilePickerFileType("Screenshot images")
        {
            Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.webp", "*.avif" },
            MimeTypes = new[] { "image/*" }
        };

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Verify WindowSnapper screenshot",
            AllowMultiple = false,
            FileTypeFilter = new[] { imageType, FilePickerFileTypes.All }
        });

        var file = files.FirstOrDefault();
        if (file is null)
            return;

        var path = file.Path.LocalPath;
        IntegrityFileText.Text = Path.GetFileName(path);
        var result = await CaptureIntegrityService.VerifyFileAsync(_settings, path);
        if (result.IsValid && !string.IsNullOrWhiteSpace(result.CaptureId))
        {
            WatermarkVerifyIdBox.Text = result.CaptureId;
            WatermarkVerifyCodeBox.Text = CaptureVerificationService.CreateVerificationCode(_settings, result.CaptureId);
        }

        if (result.BitDifference is { } bits)
        {
            var cells = result.ChangedCells is { } changedCells ? $", changed cells: {changedCells}" : string.Empty;
            var cluster = result.LargestChangedCluster is { } largestCluster ? $", largest cluster: {largestCluster}" : string.Empty;
            SettingsStatusText.Text = result.ExactPixelMismatch && bits == 0
                ? $"{result.Message} — exact pixel mismatch detected; robust descriptor rounded to 0 bits{cells}{cluster}"
                : $"{result.Message} — bit difference: {bits}{cells}{cluster}";
        }
        else
        {
            SettingsStatusText.Text = result.Message;
        }
    }

    private async void HotkeysToggle_Click(object? sender, RoutedEventArgs e)
    {
        _settings.HotkeysEnabled = HotkeysToggle.IsChecked == true;
        await SaveAndNotifyAsync();
        SettingsStatusText.Text = _settings.HotkeysEnabled
            ? $"Global hotkeys enabled — {_settings.CaptureNowHotkey} capture, {_settings.ToggleCaptureHotkey} start/stop"
            : "Screenshot hotkeys disabled";
    }

    private async void ApplyHotkeys_Click(object? sender, RoutedEventArgs e)
    {
        if (!HotkeyGesture.TryParse(CaptureHotkeyBox.Text, out var captureGesture, out var captureError))
        {
            SettingsStatusText.Text = $"Capture hotkey: {captureError}";
            return;
        }

        if (!HotkeyGesture.TryParse(ToggleHotkeyBox.Text, out var toggleGesture, out var toggleError))
        {
            SettingsStatusText.Text = $"Start/stop hotkey: {toggleError}";
            return;
        }

        if (captureGesture == toggleGesture)
        {
            SettingsStatusText.Text = "Capture and start/stop hotkeys must be different";
            return;
        }

        _settings.CaptureNowHotkey = captureGesture.DisplayText;
        _settings.ToggleCaptureHotkey = toggleGesture.DisplayText;
        CaptureHotkeyBox.Text = captureGesture.DisplayText;
        ToggleHotkeyBox.Text = toggleGesture.DisplayText;
        await SaveAndNotifyAsync();
        SettingsStatusText.Text = _settings.HotkeysEnabled
            ? $"Global hotkeys updated — {_settings.CaptureNowHotkey} / {_settings.ToggleCaptureHotkey}"
            : "Hotkeys saved; enable them to register globally";
    }

    private void TestNotification_Click(object? sender, RoutedEventArgs e)
    {
        ToastManager.ShowTest(
            NormalizeToastScale(ToastScaleSlider.Value),
            NormalizeToastDuration(ToastDurationSlider.Value));

        if (_settings.NotificationSoundEnabled)
        {
            var result = NotificationSoundService.Play(_settings.NotificationSoundPath, _settings.NotificationSoundVolume);
            SettingsStatusText.Text = result.Success ? "Notification preview shown" : $"Sound error: {result.Error}";
        }
        else
        {
            SettingsStatusText.Text = "Notification preview shown — sound muted";
        }
    }

    private void UpdateInterfaceScaleReadout()
    {
        var scale = NormalizeUiScale(InterfaceScaleSlider.Value);
        InterfaceScaleValueText.Text = $"{scale * 100:0}%";
    }

    private void UpdateToastScaleReadout()
    {
        var scale = NormalizeToastScale(ToastScaleSlider.Value);
        var width = Math.Round(BaseToastWidth * scale);
        var height = Math.Round(BaseToastHeight * scale);
        ToastScaleValueText.Text = $"{scale * 100:0}% — {width:0} × {height:0}";
    }

    private void UpdateToastDurationReadout()
    {
        var seconds = NormalizeToastDuration(ToastDurationSlider.Value);
        ToastDurationValueText.Text = $"{seconds:0.#} sec";
    }

    private void UpdateNotificationVolumeReadout()
    {
        var volume = NormalizeNotificationVolume(NotificationVolumeSlider.Value / 100.0);
        NotificationVolumeValueText.Text = $"{volume * 100:0}%";
    }

    private void UpdateSoundDisplay() =>
        SoundPathText.Text = NotificationSoundService.DisplayName(_settings.NotificationSoundPath);

    private void UpdateSoundUi() =>
        TestSoundButton.IsEnabled = SoundToggle.IsChecked == true;

    private void SaveAndNotify()
    {
        // Sliders spam events like they get paid per callback. Update the UI now; let the disk chill for a beat.
        SettingsChanged?.Invoke(_settings);

        _saveDebounceCts?.Cancel();
        var source = new CancellationTokenSource();
        _saveDebounceCts = source;
        _ = SaveDebouncedAsync(source);
    }

    private async Task SaveDebouncedAsync(CancellationTokenSource source)
    {
        try
        {
            await Task.Delay(150, source.Token);
            await SettingsStore.SaveAsync(_settings);
            SettingsStatusText.Text = "Changes saved";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            SettingsStatusText.Text = $"Could not save settings: {ex.Message}";
        }
        finally
        {
            if (ReferenceEquals(_saveDebounceCts, source))
                _saveDebounceCts = null;
            source.Dispose();
        }
    }

    private async Task SaveAndNotifyAsync()
    {
        var pending = _saveDebounceCts;
        _saveDebounceCts = null;
        pending?.Cancel();

        try
        {
            await SettingsStore.SaveAsync(_settings);
            SettingsChanged?.Invoke(_settings);
            SettingsStatusText.Text = "Changes saved";
        }
        catch (Exception ex)
        {
            SettingsStatusText.Text = $"Could not save settings: {ex.Message}";
        }
    }

    private void ApplyWindowScale(double scale)
    {
        scale = NormalizeUiScale(scale);
        SettingsScaleHost.LayoutTransform = new Avalonia.Media.ScaleTransform
        {
            ScaleX = scale,
            ScaleY = scale
        };

        MinWidth = BaseMinWindowWidth * scale;
        MinHeight = BaseMinWindowHeight * scale;
        Width = BaseWindowWidth * scale;
        Height = BaseWindowHeight * scale;

        if (IsVisible)
            FitWindowToWorkingArea(scale);
    }

    private void FitWindowToWorkingArea() =>
        FitWindowToWorkingArea(NormalizeUiScale(InterfaceScaleSlider.Value));

    private void FitWindowToWorkingArea(double scale)
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null)
            return;

        var maxWidth = Math.Max(1, screen.WorkingArea.Width / screen.Scaling - 24);
        var maxHeight = Math.Max(1, screen.WorkingArea.Height / screen.Scaling - 24);
        var wantedWidth = BaseWindowWidth * scale;
        var wantedHeight = BaseWindowHeight * scale;
        var wantedMinWidth = BaseMinWindowWidth * scale;
        var wantedMinHeight = BaseMinWindowHeight * scale;

        MaxWidth = maxWidth;
        MaxHeight = maxHeight;
        MinWidth = Math.Min(wantedMinWidth, maxWidth);
        MinHeight = Math.Min(wantedMinHeight, maxHeight);
        Width = Math.Min(wantedWidth, maxWidth);
        Height = Math.Min(wantedHeight, maxHeight);
    }

    private static double NormalizeUiScale(double scale) =>
        Math.Clamp(double.IsFinite(scale) && scale > 0 ? scale : 1, 0.8, 1.25);

    private static double NormalizeToastScale(double scale) =>
        Math.Clamp(double.IsFinite(scale) && scale > 0 ? scale : 1, 0.8, 2.0);

    private static double NormalizeToastDuration(double seconds) =>
        Math.Clamp(double.IsFinite(seconds) && seconds > 0 ? seconds : 6.0, 2.0, 15.0);

    private static double NormalizeNotificationVolume(double volume) =>
        Math.Clamp(double.IsFinite(volume) ? volume : 0.75, 0.0, 1.0);
}
