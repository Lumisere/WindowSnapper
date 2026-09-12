using System.Text.Json;
using WindowSnapper.Models;

namespace WindowSnapper.Services;

public static class SettingsStore
{
    private static readonly SemaphoreSlim SaveLock = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static string SettingsPath
    {
        get
        {
            var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(root, "WindowSnapper", "settings.json");
        }
    }

    public static async Task<CaptureSettings> LoadAsync()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return new CaptureSettings();

            await using var stream = File.OpenRead(SettingsPath);
            var settings = await JsonSerializer.DeserializeAsync<CaptureSettings>(stream, JsonOptions)
                ?? new CaptureSettings();

            // Backend IDs 2 and 10 are ghosts. Exorcise them here instead of making users debug the paranormal.
            if ((int)settings.Backend is 2 or 10)
                settings.Backend = CaptureBackend.Auto;

            if (!HotkeyGesture.TryParse(settings.CaptureNowHotkey, out var captureGesture, out _))
                HotkeyGesture.TryParse(GlobalHotkeyService.DefaultCaptureNow, out captureGesture, out _);
            if (!HotkeyGesture.TryParse(settings.ToggleCaptureHotkey, out var toggleGesture, out _))
                HotkeyGesture.TryParse(GlobalHotkeyService.DefaultToggleCapture, out toggleGesture, out _);
            if (captureGesture == toggleGesture)
                HotkeyGesture.TryParse(GlobalHotkeyService.DefaultToggleCapture, out toggleGesture, out _);

            settings.CaptureNowHotkey = captureGesture.DisplayText;
            settings.ToggleCaptureHotkey = toggleGesture.DisplayText;
            CaptureVerificationService.EnsureSecret(settings);

            return settings;
        }
        catch
        {
            return new CaptureSettings();
        }
    }

    public static async Task SaveAsync(CaptureSettings settings)
    {
        CaptureVerificationService.EnsureSecret(settings);
        await SaveLock.WaitAsync();
        try
        {
            var directory = Path.GetDirectoryName(SettingsPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            var temp = SettingsPath + ".tmp";
            await using (var stream = File.Create(temp))
                await JsonSerializer.SerializeAsync(stream, settings, JsonOptions);

            File.Move(temp, SettingsPath, true);
        }
        finally
        {
            SaveLock.Release();
        }
    }
}
