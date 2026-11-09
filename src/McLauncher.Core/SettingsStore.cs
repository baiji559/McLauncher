using System.Text.Json;

namespace McLauncher.Core;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _applicationDirectory;
    private readonly bool _portable;

    public string FilePath { get; }
    public SettingsStore(string? filePath = null, string? applicationDirectory = null)
    {
        _applicationDirectory = Path.GetFullPath(applicationDirectory ?? AppContext.BaseDirectory);
        var portable = Path.Combine(_applicationDirectory, "lattice.portable.json");
        _portable = filePath is null && (File.Exists(portable) || FindNeighborGame() is not null);
        FilePath = Path.GetFullPath(filePath ?? (_portable ? portable : Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "McLauncher", "settings.json")));
    }

    public LauncherSettings Load()
    {
        var settings = new LauncherSettings();
        try
        {
            if (File.Exists(FilePath)) settings = JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllText(FilePath), JsonOptions) ?? settings;
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException) { }
        var path = new LauncherSettings().GameDirectory;
        var validPath = false;
        try
        {
            if (!string.IsNullOrWhiteSpace(settings.GameDirectory))
            {
                path = Environment.ExpandEnvironmentVariables(settings.GameDirectory);
                path = Path.IsPathFullyQualified(path) ? Path.GetFullPath(path)
                    : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(FilePath)!, path));
                validPath = true;
            }
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or IOException) { path = new LauncherSettings().GameDirectory; }
        if ((!validPath || !File.Exists(FilePath) || !Directory.Exists(path)) && FindNeighborGame() is { } neighboring)
            path = neighboring;
        settings.GameDirectory = path;
        if (string.IsNullOrWhiteSpace(settings.Version)) settings.Version = "release";
        if (string.IsNullOrWhiteSpace(settings.OfflineUsername)) settings.OfflineUsername = "Player";
        return settings;
    }

    public void Save(LauncherSettings settings)
    {
        var directory = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(directory);
        var temporaryFile = FilePath + ".tmp";
        var json = JsonSerializer.SerializeToNode(settings, JsonOptions)!;
        if (_portable && !string.IsNullOrWhiteSpace(settings.GameDirectory))
        {
            try { json[nameof(LauncherSettings.GameDirectory)] = Path.GetRelativePath(_applicationDirectory, settings.GameDirectory); }
            catch (Exception e) when (e is ArgumentException or NotSupportedException or IOException) { }
        }
        File.WriteAllText(temporaryFile, json.ToJsonString(JsonOptions));
        File.Move(temporaryFile, FilePath, true);
    }

    private string? FindNeighborGame()
    {
        foreach (var directory in new[] { _applicationDirectory, Path.GetDirectoryName(_applicationDirectory) })
        {
            if (directory is null) continue;
            var nested = Path.Combine(directory, ".minecraft");
            if (Directory.Exists(nested)) return nested;
            if (Directory.Exists(Path.Combine(directory, "versions"))) return directory;
        }
        return null;
    }
}
