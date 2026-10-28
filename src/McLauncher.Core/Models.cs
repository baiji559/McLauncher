namespace McLauncher.Core;

public sealed record MinecraftVersion(string Id, DateTimeOffset? ReleasedAt);

public sealed record MinecraftAccount(string Username, string Uuid);

public sealed record MinecraftLocation(string MainDirectory, string SelectedDirectory, string? SuggestedVersion)
{
    public static MinecraftLocation Resolve(string path)
    {
        var selected = Path.GetFullPath(path);
        var directory = new DirectoryInfo(selected);

        if (directory.Name.Equals("versions", StringComparison.OrdinalIgnoreCase) && directory.Parent is not null)
            return new MinecraftLocation(directory.Parent.FullName, directory.Parent.FullName, null);

        if (directory.Parent?.Name.Equals("versions", StringComparison.OrdinalIgnoreCase) == true
            && directory.Parent.Parent is not null
            && File.Exists(Path.Combine(selected, directory.Name + ".json")))
            return new MinecraftLocation(directory.Parent.Parent.FullName, selected, directory.Name);

        return new MinecraftLocation(selected, selected, null);
    }

    public string GetGameDirectory(string version)
    {
        if (SuggestedVersion == version) return SelectedDirectory;

        var versionDirectory = Path.Combine(MainDirectory, "versions", version);
        return Directory.Exists(Path.Combine(versionDirectory, "mods"))
            || Directory.Exists(Path.Combine(versionDirectory, "saves"))
            || File.Exists(Path.Combine(versionDirectory, "options.txt"))
            ? versionDirectory
            : MainDirectory;
    }
}

public sealed class LauncherSettings
{
    public string GameDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "McLauncher", "game");

    public string Version { get; set; } = "release";
    public int MemoryMb { get; set; } = 4096;
    public bool UseMicrosoftAccount { get; set; } = true;
    public string OfflineUsername { get; set; } = "Player";
    public string? MicrosoftAccountUuid { get; set; }
    public bool ContentUseSystemProxy { get; set; } = true;
}

