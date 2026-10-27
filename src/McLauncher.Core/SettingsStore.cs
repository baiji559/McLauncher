using System.Text.Json;

namespace McLauncher.Core;

public sealed class SettingsStore(string? filePath = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public string FilePath { get; } = filePath ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "McLauncher", "settings.json");

    public LauncherSettings Load()
    {
        if (!File.Exists(FilePath)) return new LauncherSettings();

        try
        {
            return JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllText(FilePath), JsonOptions)
                ?? new LauncherSettings();
        }
        catch (JsonException)
        {
            return new LauncherSettings();
        }
    }

    public void Save(LauncherSettings settings)
    {
        var directory = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(directory);
        var temporaryFile = FilePath + ".tmp";
        File.WriteAllText(temporaryFile, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(temporaryFile, FilePath, true);
    }
}

