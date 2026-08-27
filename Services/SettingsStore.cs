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

            // Backend value 10 belonged to an older Linux capture option that is no longer available.
            if ((int)settings.Backend == 10)
                settings.Backend = CaptureBackend.Auto;

            return settings;
        }
        catch
        {
            return new CaptureSettings();
        }
    }

    public static async Task SaveAsync(CaptureSettings settings)
    {
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
