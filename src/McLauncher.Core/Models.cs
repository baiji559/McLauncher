namespace McLauncher.Core;

public sealed record MinecraftVersion(string Id, DateTimeOffset? ReleasedAt);

public sealed record MinecraftAccount(string Username, string Uuid);

public sealed record MinecraftLocation(string MainDirectory, string SelectedDirectory, string? SuggestedVersion)
{
    public static MinecraftLocation Resolve(string path)
    {
        var selected = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var directory = new DirectoryInfo(selected);

        if (directory.Name.Equals("versions", StringComparison.OrdinalIgnoreCase) && directory.Parent is not null)
            return new MinecraftLocation(directory.Parent.FullName, directory.Parent.FullName, null);

        // Folder pickers may land in an instance's natives, mods or saves subdirectory.
        // Recover the containing instance before looking for a shared game root.
        for (var ancestor = directory; ancestor is not null; ancestor = ancestor.Parent)
            if (ancestor.Parent?.Name.Equals("versions", StringComparison.OrdinalIgnoreCase) == true
                && ancestor.Parent.Parent is { } root)
                return new MinecraftLocation(root.FullName, ancestor.FullName, ancestor.Name);

        for (var ancestor = directory; ancestor is not null; ancestor = ancestor.Parent)
        {
            var relative = Path.GetRelativePath(ancestor.FullName, selected).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
            var knownChild = new[] { "assets", "libraries", "mods", "saves", "resourcepacks", "shaderpacks", "modpacks", "logs", "runtime" }
                .Contains(relative, StringComparer.OrdinalIgnoreCase);
            if (ancestor.Name.Equals(".minecraft", StringComparison.OrdinalIgnoreCase)
                || (Directory.Exists(Path.Combine(ancestor.FullName, "versions")) && (relative == "." || knownChild)))
                return new MinecraftLocation(ancestor.FullName, ancestor.FullName, null);
        }

        var nested = Path.Combine(selected, ".minecraft");
        if (Directory.Exists(nested)) return Resolve(nested);

        return new MinecraftLocation(selected, selected, null);
    }

    public string GetGameDirectory(string version)
    {
        var versionDirectory = Path.Combine(MainDirectory, "versions", version);
        // PCL stores an explicit per-version isolation choice as 1 (isolated) or 2 (shared).
        var pclSetup = Path.Combine(versionDirectory, "PCL", "Setup.ini");
        try
        {
            if (File.Exists(pclSetup))
                foreach (var line in File.ReadLines(pclSetup))
                {
                    var setting = line.Split(':', 2);
                    if (setting.Length != 2 || !setting[0].Trim().Equals("VersionArgumentIndie", StringComparison.OrdinalIgnoreCase)) continue;
                    if (setting[1].Trim() == "1") return versionDirectory;
                    if (setting[1].Trim() == "2") return MainDirectory;
                }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        if (SuggestedVersion == version) return SelectedDirectory;
        return Directory.Exists(Path.Combine(versionDirectory, "mods"))
            || Directory.Exists(Path.Combine(versionDirectory, "saves"))
            || Directory.Exists(Path.Combine(versionDirectory, "shaderpacks"))
            || Directory.Exists(Path.Combine(versionDirectory, "resourcepacks"))
            || Directory.Exists(Path.Combine(versionDirectory, "modpacks"))
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
    public bool? ReduceMotion { get; set; }
}
