using Retrace.Core;
using System.Text.Json;

namespace Retrace.Data;

public static class RetracePaths
{
    public static string DataDirectory => Ensure(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Retrace"));
    public static string DatabasePath => Path.Combine(DataDirectory, "retrace.db");
    public static string RecoveryDirectory => Ensure(Path.Combine(DataDirectory, "Recovery"));
    public static string BaselineDirectory => Ensure(Path.Combine(RecoveryDirectory, "Baseline"));
    public static string VersionsDirectory => Ensure(Path.Combine(RecoveryDirectory, "Versions"));
    public static string SnapshotsDirectory => Ensure(Path.Combine(RecoveryDirectory, "Snapshots"));
    public static string SettingsPath => Path.Combine(DataDirectory, "settings.json");
    public static string LogPath => Path.Combine(DataDirectory, "retrace.log");

    private static string Ensure(string path) { Directory.CreateDirectory(path); return path; }
}

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public async Task<RetraceSettings> LoadAsync()
    {
        try
        {
            if (File.Exists(RetracePaths.SettingsPath))
            {
                var json = await File.ReadAllTextAsync(RetracePaths.SettingsPath);
                var existing = JsonSerializer.Deserialize<RetraceSettings>(json, JsonOptions);
                if (existing is not null) return Normalize(existing);
            }
        }
        catch { }

        var testFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "RetraceTest");
        Directory.CreateDirectory(testFolder);
        var defaults = new RetraceSettings
        {
            WatchedFolders = new List<string> { testFolder }
        };
        await SaveAsync(defaults);
        return defaults;
    }

    public async Task SaveAsync(RetraceSettings settings)
    {
        settings = Normalize(settings);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        await File.WriteAllTextAsync(RetracePaths.SettingsPath, json);
    }

    private static RetraceSettings Normalize(RetraceSettings settings)
    {
        settings.WatchedFolders = settings.WatchedFolders
            .Where(p => !string.IsNullOrWhiteSpace(p)).Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        settings.ExcludedFolders = settings.ExcludedFolders
            .Where(p => !string.IsNullOrWhiteSpace(p)).Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        settings.MaxRecoveryStorageGb = Math.Clamp(settings.MaxRecoveryStorageGb, 1, 100);
        settings.KeepHistoryDays = Math.Clamp(settings.KeepHistoryDays, 1, 3650);
        settings.MaxTrackedFileSizeMb = Math.Clamp(settings.MaxTrackedFileSizeMb, 1, 2048);
        return settings;
    }
}
