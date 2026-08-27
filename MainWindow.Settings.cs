using WindowSnapper.Models;
using WindowSnapper.Services;

namespace WindowSnapper;

public sealed partial class MainWindow
{
    private void ApplySettings(CaptureSettings settings)
    {
        SelectComboValue(TargetModeCombo, settings.TargetMode);
        SelectBackend(settings.Backend);
        SelectComboValue(FormatCombo, settings.ImageFormat);
        SelectComboValue(NotificationModeCombo, ResolveNotificationMode(settings));

        TargetValueBox.Text = settings.TargetValue;
        _selectedNativeWindowId = settings.TargetNativeId ?? string.Empty;
        _selectedNativeTargetValue = settings.TargetValue?.Trim() ?? string.Empty;
        IntervalBox.Text = settings.IntervalMinutes.ToString("0.##");
        OutputFolderBox.Text = settings.OutputFolder;
        PrefixBox.Text = settings.FileNamePrefix;
        ScreenshotEveryBox.Text = Math.Max(1, settings.ScreenshotToastEvery).ToString();
        ReminderIntervalBox.Text = settings.NotificationIntervalMinutes.ToString("0.##");

        ApplyUiScale(NormalizeUiScale(settings.UiScale), false);
        UpdateNotificationUi();
    }

    private CaptureSettings ReadSettings(bool validate, out string error)
    {
        error = string.Empty;

        if (!TryReadDouble(IntervalBox.Text, out var interval) || interval <= 0)
        {
            if (validate)
                error = "Screenshot interval must be greater than zero";
            interval = Math.Max(0.05, _settings.IntervalMinutes);
        }

        if (!TryReadDouble(ReminderIntervalBox.Text, out var reminderInterval) || reminderInterval <= 0)
        {
            if (validate && SelectedValue(NotificationModeCombo, NotificationTriggerMode.Off) == NotificationTriggerMode.TimedReminder)
                error = "Reminder interval must be greater than zero";
            reminderInterval = Math.Max(0.05, _settings.NotificationIntervalMinutes);
        }

        if (!int.TryParse(ScreenshotEveryBox.Text, out var everyScreenshots) || everyScreenshots <= 0)
        {
            if (validate && SelectedValue(NotificationModeCombo, NotificationTriggerMode.Off) == NotificationTriggerMode.EveryScreenshots)
                error = "Screenshot notification count must be at least 1";
            everyScreenshots = Math.Max(1, _settings.ScreenshotToastEvery);
        }

        var outputFolder = OutputFolderBox.Text?.Trim() ?? string.Empty;
        if (validate && outputFolder.Length == 0)
            error = "Choose an output folder";

        var backend = SelectedValue(BackendCombo, CaptureBackend.Auto);
        var targetValue = TargetValueBox.Text?.Trim() ?? string.Empty;
        var targetNativeId = string.Equals(
            targetValue,
            _selectedNativeTargetValue,
            StringComparison.OrdinalIgnoreCase)
            ? _selectedNativeWindowId
            : string.Empty;
        var targetRequired = CaptureBackendInfo.RequiresWindowTarget(backend);

        if (validate && targetRequired && targetValue.Length == 0)
            error = "Choose a target application";

        if (validate && targetRequired && targetValue.Length > 0)
        {
            var resolvedTarget = !string.IsNullOrWhiteSpace(targetNativeId)
                ? WindowFinder.ResolveNativeId(targetNativeId)
                : null;
            resolvedTarget ??= WindowFinder.Resolve(
                SelectedValue(TargetModeCombo, TargetMode.ProcessExe),
                targetValue);

            if (resolvedTarget is null)
                error = "Target window not found";
        }

        var mode = SelectedValue(NotificationModeCombo, NotificationTriggerMode.Off);
        return new CaptureSettings
        {
            TargetMode = SelectedValue(TargetModeCombo, TargetMode.ProcessExe),
            TargetValue = targetValue,
            TargetNativeId = targetNativeId,
            IntervalMinutes = interval,
            OutputFolder = outputFolder,
            Backend = backend,
            ImageFormat = SelectedValue(FormatCombo, ImageFormatChoice.Png),
            UiScale = NormalizeUiScale(_settings.UiScale),
            ResolutionScale = 1.0,
            FileNamePrefix = string.IsNullOrWhiteSpace(PrefixBox.Text) ? "capture" : PrefixBox.Text.Trim(),
            NotificationMode = mode,
            ScreenshotToastEvery = everyScreenshots,
            ToastScale = NormalizeToastScale(_settings.ToastScale),
            ToastDurationSeconds = NormalizeToastDuration(_settings.ToastDurationSeconds),
            NotificationIntervalMinutes = reminderInterval,
            NotificationSoundPath = _settings.NotificationSoundPath ?? string.Empty,
            NotificationSoundEnabled = _settings.NotificationSoundEnabled,
            NotificationSoundVolume = NormalizeNotificationVolume(_settings.NotificationSoundVolume),
            CopyLatestToClipboard = _settings.CopyLatestToClipboard,
            NormalizeHdrCaptures = _settings.NormalizeHdrCaptures,
            CaptureCursor = _settings.CaptureCursor,

            // Keep legacy values in sync when saving.
            ScreenshotToastEnabled = mode == NotificationTriggerMode.EveryScreenshots,
            TimedNotificationEnabled = mode == NotificationTriggerMode.TimedReminder,
            ToastWidth = 344 * NormalizeToastScale(_settings.ToastScale),
            ToastHeight = 82 * NormalizeToastScale(_settings.ToastScale)
        };
    }

    private void SelectBackend(CaptureBackend requested)
    {
        if (BackendCombo.ItemsSource is not IEnumerable<ComboOption<CaptureBackend>> options)
            return;

        var match = options.FirstOrDefault(option => EqualityComparer<CaptureBackend>.Default.Equals(option.Value, requested));
        BackendCombo.SelectedItem = match ?? options.FirstOrDefault();
    }

    private static void SelectComboValue<T>(Avalonia.Controls.ComboBox combo, T value)
    {
        if (combo.ItemsSource is not IEnumerable<ComboOption<T>> options)
            return;

        combo.SelectedItem = options.FirstOrDefault(option => EqualityComparer<T>.Default.Equals(option.Value, value));
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

    private static double NormalizeToastScale(double scale) =>
        Math.Clamp(double.IsFinite(scale) && scale > 0 ? scale : 1.0, 0.8, 2.0);

    private static double NormalizeToastDuration(double seconds) =>
        Math.Clamp(double.IsFinite(seconds) && seconds > 0 ? seconds : 6.0, 2.0, 15.0);



    private static double NormalizeNotificationVolume(double volume) =>
        Math.Clamp(double.IsFinite(volume) ? volume : 0.75, 0.0, 1.0);
}
