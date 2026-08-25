using System.IO;
using System.Text.Json;
using WindowSnapper.Models;

namespace WindowSnapper.Services;

public static class SettingsStore
{
    private static readonly string SettingsFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WindowSnapper");

    private static readonly string SettingsFile = Path.Combine(SettingsFolder, "settings.json");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static void Save(CaptureSettings settings)
    {
        Directory.CreateDirectory(SettingsFolder);
        File.WriteAllText(SettingsFile, JsonSerializer.Serialize(settings, JsonOptions));
    }

    public static async Task SaveAsync(CaptureSettings settings)
    {
        Directory.CreateDirectory(SettingsFolder);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        await File.WriteAllTextAsync(SettingsFile, json);
    }

    public static async Task<CaptureSettings> LoadAsync()
    {
        if (!File.Exists(SettingsFile))
            return new CaptureSettings();

        try
        {
            var json = await File.ReadAllTextAsync(SettingsFile);
            return JsonSerializer.Deserialize<CaptureSettings>(json) ?? new CaptureSettings();
        }
        catch (JsonException)
        {
            return new CaptureSettings();
        }
        catch (IOException)
        {
            return new CaptureSettings();
        }
    }
}
