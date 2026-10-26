using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ferry.Core;

/// <summary>Saves settings and the download list as JSON under %APPDATA%\Ferry.</summary>
public static class Storage
{
    public static string DataFolder { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Ferry");

    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    static string SettingsPath => Path.Combine(DataFolder, "settings.json");
    static string DownloadsPath => Path.Combine(DataFolder, "downloads.json");

    public static Settings LoadSettings() => (Read<Settings>(SettingsPath) ?? new Settings()).Normalize();
    public static void SaveSettings(Settings s) => Write(SettingsPath, s);

    public static List<DownloadItem> LoadDownloads() => Read<List<DownloadItem>>(DownloadsPath) ?? new();
    public static void SaveDownloads(IEnumerable<DownloadItem> items) => Write(DownloadsPath, items.ToList());

    static T? Read<T>(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options) : default;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // Keep the broken file for inspection and start fresh.
            try { File.Copy(path, path + ".bad", true); } catch { }
            return default;
        }
    }

    static readonly object WriteLock = new();

    static void Write<T>(string path, T value)
    {
        lock (WriteLock)
        {
            Directory.CreateDirectory(DataFolder);
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(value, Options));
            File.Move(tmp, path, true);
        }
    }
}
