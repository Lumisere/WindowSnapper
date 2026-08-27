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

    private readonly CaptureSettings _settings;
    private bool _loading = true;

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

        UpdateInterfaceScaleReadout();
        UpdateToastScaleReadout();
        UpdateToastDurationReadout();
        UpdateNotificationVolumeReadout();
        UpdateSoundDisplay();
        UpdateSoundUi();
        _loading = false;
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        Close();
    }

    private void InterfaceScaleSlider_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        UpdateInterfaceScaleReadout();
        if (_loading)
            return;

        _settings.UiScale = NormalizeUiScale(InterfaceScaleSlider.Value);
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

    private async void ClipboardToggle_Click(object? sender, RoutedEventArgs e)
    {
        _settings.CopyLatestToClipboard = ClipboardToggle.IsChecked == true;
        await SaveAndNotifyAsync();
        SettingsStatusText.Text = _settings.CopyLatestToClipboard
            ? "New captures will be copied to the clipboard"
            : "Clipboard copy disabled";
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

    private async void SaveAndNotify()
    {
        await SaveAndNotifyAsync();
    }

    private async Task SaveAndNotifyAsync()
    {
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

    private static double NormalizeUiScale(double scale) =>
        Math.Clamp(double.IsFinite(scale) && scale > 0 ? scale : 1, 0.8, 1.25);

    private static double NormalizeToastScale(double scale) =>
        Math.Clamp(double.IsFinite(scale) && scale > 0 ? scale : 1, 0.8, 2.0);

    private static double NormalizeToastDuration(double seconds) =>
        Math.Clamp(double.IsFinite(seconds) && seconds > 0 ? seconds : 6.0, 2.0, 15.0);


    private static double NormalizeNotificationVolume(double volume) =>
        Math.Clamp(double.IsFinite(volume) ? volume : 0.75, 0.0, 1.0);
}
