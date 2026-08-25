using System.Globalization;
using System.Windows.Controls;
using WindowSnapper.Models;
using WindowSnapper.Services;

namespace WindowSnapper;

public partial class MainWindow
{
    private void ApplySettings(CaptureSettings settings)
    {
        SelectOption(TargetModeCombo, settings.TargetMode);
        SelectOption(BackendCombo, settings.Backend);
        SelectOption(FormatCombo, settings.ImageFormat);

        TargetValueBox.Text = settings.TargetValue;
        IntervalBox.Text = settings.IntervalMinutes.ToString("0.##", CultureInfo.InvariantCulture);
        FolderBox.Text = settings.OutputFolder;
        PrefixBox.Text = settings.FileNamePrefix;

        _updatingNotificationControls = true;
        try
        {
            SelectOption(NotificationModeCombo, ResolveNotificationMode(settings));
            ScreenshotToastEveryBox.Text = Math.Max(1, settings.ScreenshotToastEvery).ToString(CultureInfo.InvariantCulture);
            SoundIntervalBox.Text = settings.NotificationIntervalMinutes.ToString("0.##", CultureInfo.InvariantCulture);
            _soundPath = settings.NotificationSoundPath ?? string.Empty;
            NotificationSoundToggle.IsChecked = settings.NotificationSoundEnabled;
            UpdateSoundPathDisplay();
            UpdateSoundEnabledUi();

            ToastScaleSlider.Value = GetToastScale(settings);
            UpdateToastScaleReadout();
            UpdateNotificationModeUi();
        }
        finally
        {
            _updatingNotificationControls = false;
        }
    }
    // Reads the current settings from the UI controls, optionally validating them (cuz yknow good practice or whatever)
    private CaptureSettings ReadSettings(bool validate, out string error)
    {
        var screenshotMinutes = ParseMinutes(IntervalBox.Text, _settings.IntervalMinutes, out var screenshotIntervalOk);
        var reminderMinutes = ParseMinutes(SoundIntervalBox.Text, _settings.NotificationIntervalMinutes, out var reminderIntervalOk);
        var toastEvery = ParsePositiveInt(ScreenshotToastEveryBox.Text, _settings.ScreenshotToastEvery, out var toastEveryOk);
        var toastScale = NormalizeToastScale(ToastScaleSlider?.Value ?? GetToastScale(_settings));

        var settings = new CaptureSettings
        {
            TargetMode = SelectedValue(TargetModeCombo, TargetMode.ProcessExe),
            TargetValue = TargetValueBox.Text.Trim(),
            IntervalMinutes = screenshotMinutes,
            OutputFolder = FolderBox.Text.Trim(),
            Backend = SelectedValue(BackendCombo, CaptureBackend.Auto),
            ImageFormat = SelectedValue(FormatCombo, ImageFormatChoice.Png),
            FileNamePrefix = string.IsNullOrWhiteSpace(PrefixBox.Text) ? "capture" : PrefixBox.Text.Trim(),

            NotificationMode = SelectedValue(NotificationModeCombo, NotificationTriggerMode.Off),
            ScreenshotToastEvery = toastEvery,
            ToastScale = toastScale,
            ToastWidth = BaseToastWidth * toastScale,
            ToastHeight = BaseToastHeight * toastScale,

            NotificationIntervalMinutes = reminderMinutes,
            NotificationSoundPath = _soundPath,
            NotificationSoundEnabled = NotificationSoundToggle?.IsChecked != false
        };

        settings.ScreenshotToastEnabled = settings.NotificationMode == NotificationTriggerMode.EveryScreenshots;
        settings.TimedNotificationEnabled = settings.NotificationMode == NotificationTriggerMode.TimedReminder;

        error = string.Empty;
        if (!validate)
            return settings;

        if (!screenshotIntervalOk)
            error = "Screenshot interval must be at least 0.05 minutes (3 seconds).";
        else if (settings.NotificationMode == NotificationTriggerMode.EveryScreenshots && !toastEveryOk)
            error = "Screenshot notification frequency must be 1 or more screenshots.";
        else if (settings.NotificationMode == NotificationTriggerMode.TimedReminder && !reminderIntervalOk)
            error = "Reminder interval must be at least 0.05 minutes (3 seconds).";
        else if (settings.TargetValue.Length == 0)
            error = "Choose an application window or enter a target value.";
        else if (settings.OutputFolder.Length == 0)
            error = "Choose a folder for screenshots.";
        else if (WindowFinder.Resolve(settings.TargetMode, settings.TargetValue) == IntPtr.Zero)
            error = "The target window is not currently available.";

        return settings;
    }

    private static double ParseMinutes(string text, double fallback, out bool valid)
    {
        valid = double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                && double.IsFinite(value)
                && value >= 0.05;

        if (valid)
            return value;

        var safeFallback = double.IsFinite(fallback) && fallback >= 0.05 ? fallback : 5;
        return safeFallback;
    }

    private static int ParsePositiveInt(string text, int fallback, out bool valid)
    {
        valid = int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                && value >= 1;

        return valid ? value : Math.Max(1, fallback);
    }

    private async Task SaveSettingsAsync(CaptureSettings settings)
    {
        await SettingsStore.SaveAsync(settings);
        _settings = settings;
    }

    private static T SelectedValue<T>(ComboBox combo, T fallback) where T : struct, Enum
    {
        return combo.SelectedItem is ComboOption<T> item ? item.Value : fallback;
    }

    private static void SelectOption<T>(ComboBox combo, T value) where T : struct, Enum
    {
        foreach (var item in combo.Items.OfType<ComboOption<T>>())
        {
            if (!EqualityComparer<T>.Default.Equals(item.Value, value))
                continue;

            combo.SelectedItem = item;
            break;
        }
    }

    private static NotificationTriggerMode ResolveNotificationMode(CaptureSettings settings)
    {
        if (settings.NotificationMode.HasValue)
            return settings.NotificationMode.Value;

        if (settings.TimedNotificationEnabled && !settings.ScreenshotToastEnabled)
            return NotificationTriggerMode.TimedReminder;

        if (settings.ScreenshotToastEnabled)
            return NotificationTriggerMode.EveryScreenshots;

        if (settings.TimedNotificationEnabled)
            return NotificationTriggerMode.TimedReminder;

        return NotificationTriggerMode.Off;
    }

    private static double NormalizeToastScale(double scale)
    {
        if (!double.IsFinite(scale) || scale <= 0)
            scale = 1;

        return Math.Clamp(scale, MinToastScale, MaxToastScale);
    }

    private static double GetToastScale(CaptureSettings settings)
    {
        if (double.IsFinite(settings.ToastScale) && settings.ToastScale > 0)
            return NormalizeToastScale(settings.ToastScale);

        var widthScale = settings.ToastWidth > 0 ? settings.ToastWidth / BaseToastWidth : 1;
        var heightScale = settings.ToastHeight > 0 ? settings.ToastHeight / BaseToastHeight : 1;
        return NormalizeToastScale((widthScale + heightScale) / 2);
    }

    private void ToastScaleSlider_ValueChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<double> e)
    {
        UpdateToastScaleReadout();
    }

    private void UpdateToastScaleReadout()
    {
        if (ToastScaleSlider is null || ToastScaleValueText is null)
            return;

        var scale = NormalizeToastScale(ToastScaleSlider.Value);
        var width = Math.Round(BaseToastWidth * scale);
        var height = Math.Round(BaseToastHeight * scale);
        ToastScaleValueText.Text = $"{scale * 100:0}%  ·  {width:0} × {height:0}";
    }
}
