namespace McLauncher.Core;

public sealed record LocalContentFile(string FullPath, ContentKind Kind, string InstanceName, long Size, DateTime ModifiedUtc)
{
    public string FileName => Path.GetFileName(FullPath);
    public string KindLabel => Kind switch { ContentKind.Mod => "模组", ContentKind.Modpack => "整合包", ContentKind.Shader => "光影", _ => "资源包" };
    public static string FormatSize(long size) => size < 1024 ? $"{size} B" : size < 1048576 ? $"{size / 1024d:0.##} KB" : $"{size / 1048576d:0.##} MB";
    public string Detail => $"{KindLabel} · {InstanceName} · {FormatSize(Size)} · {ModifiedUtc.ToLocalTime():yyyy-MM-dd HH:mm}";
}

public sealed record LocalContentSnapshot(IReadOnlyList<LocalContentFile> Files, IReadOnlyList<string> Warnings);

public static class LocalContentLibrary
{
    public static IReadOnlyList<string> GameDirectories(MinecraftLocation location)
    {
        var directories = new List<string> { location.MainDirectory };
        var versions = Path.Combine(location.MainDirectory, "versions");
        if (Directory.Exists(versions) && !IsLink(versions))
            directories.AddRange(Directory.EnumerateDirectories(versions).Where(path => !IsLink(path)));
        return directories;
    }

    public static LocalContentSnapshot Scan(string gamePath)
    {
        var files = new List<LocalContentFile>();
        var warnings = new List<string>();
        var location = MinecraftLocation.Resolve(gamePath);
        IReadOnlyList<string> directories;
        try { directories = GameDirectories(location); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { directories = [location.MainDirectory]; warnings.Add(e.Message); }
        foreach (var game in directories)
        foreach (var kind in Enum.GetValues<ContentKind>())
        {
            var folder = ContentDownloader.GetDestinationDirectory(game, kind);
            try
            {
                if (!Directory.Exists(folder) || IsLink(game) || IsLink(folder)) continue;
                foreach (var path in Directory.EnumerateFiles(folder))
                {
                    try
                    {
                        if (!AcceptsFile(kind, path) || IsLink(path)) continue;
                        var info = new FileInfo(path);
                        files.Add(new(info.FullName, kind, string.Equals(game, location.MainDirectory, StringComparison.OrdinalIgnoreCase)
                            ? "共享游戏目录" : Path.GetFileName(game), info.Length, info.LastWriteTimeUtc));
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException) { warnings.Add($"{path}：{e.Message}"); }
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { warnings.Add($"{folder}：{e.Message}"); }
        }
        return new(files.OrderByDescending(file => file.ModifiedUtc).ThenBy(file => file.FileName).ToArray(), warnings);
    }

    // Revalidate the exact file and its directory at deletion time, even when the snapshot is stale.
    public static string ValidateFile(string gamePath, LocalContentFile file)
    {
        var root = MinecraftLocation.Resolve(gamePath).MainDirectory;
        var path = Path.GetFullPath(file.FullPath);
        var folder = Path.GetDirectoryName(path)!;
        var game = Path.GetDirectoryName(folder)!;
        var expected = ContentDownloader.GetDestinationDirectory(game, file.Kind);
        if (!string.Equals(folder, expected, StringComparison.OrdinalIgnoreCase) || !AcceptsFile(file.Kind, path)
            || !(string.Equals(game, root, StringComparison.OrdinalIgnoreCase)
                || string.Equals(Path.GetDirectoryName(game), Path.Combine(root, "versions"), StringComparison.OrdinalIgnoreCase)))
            throw new IOException("文件不在当前游戏目录的资源文件夹中，请刷新列表。");
        for (var ancestor = new DirectoryInfo(folder); ancestor is not null; ancestor = ancestor.Parent)
        {
            if (IsLink(ancestor.FullName)) throw new IOException("此资源位于链接目录中，请从文件管理器处理。");
            if (string.Equals(ancestor.FullName, root, StringComparison.OrdinalIgnoreCase)) break;
        }
        var info = new FileInfo(path);
        if (!info.Exists) throw new FileNotFoundException("文件已不存在，请刷新列表。", path);
        if (IsLink(path) || info.Length != file.Size || info.LastWriteTimeUtc != file.ModifiedUtc)
            throw new IOException("文件已发生变化，请刷新列表后重新选择。");
        return path;
    }

    private static bool IsLink(string path) => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
    private static bool AcceptsFile(ContentKind kind, string path) => kind switch
    {
        ContentKind.Mod => path.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase),
        ContentKind.Modpack => path.EndsWith(".mrpack", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase),
        _ => path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
    };
}
