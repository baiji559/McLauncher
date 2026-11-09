using McLauncher.Core;

internal static class LocalContentTests
{
    public static void LibraryAndLocations()
    {
        var root = Path.GetFullPath(Path.Combine("artifacts", "tests", "local-content-" + Guid.NewGuid().ToString("N")));
        var game = Directory.CreateDirectory(Path.Combine(root, ".minecraft")).FullName;
        var first = Directory.CreateDirectory(Path.Combine(game, "versions", "1.18.2")).FullName;
        var second = Directory.CreateDirectory(Path.Combine(game, "versions", "另一个实例")).FullName;
        var natives = Directory.CreateDirectory(Path.Combine(first, "1.18.2-natives")).FullName;
        File.WriteAllText(Path.Combine(first, "1.18.2.json"), "{}");
        void Add(string instance, string folder, string file)
        { var directory = Directory.CreateDirectory(Path.Combine(instance, folder)); File.WriteAllText(Path.Combine(directory.FullName, file), "fixture"); }
        Add(game, "resourcepacks", "共享资源.zip");
        Add(first, "mods", "enabled.jar"); Add(first, "mods", "disabled.jar.disabled");
        Add(first, "mods", "config.json"); Add(first, "mods", ".mclauncher-test.part");
        Add(second, "shaderpacks", "shaders.zip"); Add(second, "modpacks", "pack.mrpack");
        var resolved = MinecraftLocation.Resolve(natives);
        Require(resolved.MainDirectory == game && resolved.SelectedDirectory == first && resolved.SuggestedVersion == "1.18.2", "natives 子目录应恢复到隔离实例与正确根目录。");
        Require(MinecraftLocation.Resolve(root).MainDirectory == game, "选择 PCL2 上层目录应识别 .minecraft。");
        Require(MinecraftLocation.Resolve(Path.Combine(first, "mods")).SelectedDirectory == first, "资源子目录应识别为所属实例。");
        Require(MinecraftLocation.Resolve(game).GetGameDirectory("另一个实例") == second, "仅有光影和整合包的实例也应识别隔离目录。");
        var snapshot = LocalContentLibrary.Scan(natives);
        Require(snapshot.Files.Count == 5 && snapshot.Warnings.Count == 0 && snapshot.Files.Any(file => file.InstanceName == "另一个实例")
            && snapshot.Files.Any(file => file.InstanceName == "共享游戏目录"), "应汇总共享目录与全部实例，只识别资源文件并排除配置和临时文件。");
        var entry = snapshot.Files.Single(file => file.FileName == "enabled.jar");
        Require(LocalContentLibrary.ValidateFile(game, entry) == entry.FullPath, "真实扫描文件应通过删除前校验。");
        var outside = Path.Combine(root, "outside.jar"); File.WriteAllText(outside, "fixture");
        Reject(() => LocalContentLibrary.ValidateFile(game, entry with { FullPath = outside }));
        File.WriteAllText(entry.FullPath, "replaced with a different file");
        Reject(() => LocalContentLibrary.ValidateFile(game, entry));
        Require(File.ReadAllText(outside) == "fixture", "校验不得修改文件。");
        var pcl = Directory.CreateDirectory(Path.Combine(game, "versions", "PCL 空实例", "PCL")).FullName;
        File.WriteAllText(Path.Combine(pcl, "Setup.ini"), "VersionArgumentIndie:1");
        Require(MinecraftLocation.Resolve(game).GetGameDirectory("PCL 空实例") == Path.GetDirectoryName(pcl), "PCL 明确启用版本隔离时，即使没有资源目录，也应向实例下载。");
        File.WriteAllText(Path.Combine(pcl, "Setup.ini"), "VersionArgumentIndie:2");
        Require(MinecraftLocation.Resolve(game).GetGameDirectory("PCL 空实例") == game, "PCL 明确使用共享目录时应遵循该设置。");
        var portableRoot = Directory.CreateDirectory(Path.Combine(root, "portable-original")).FullName;
        var portableGame = Directory.CreateDirectory(Path.Combine(portableRoot, ".minecraft", "versions", "PCL 实例")).FullName;
        var store = new SettingsStore(applicationDirectory: portableRoot);
        store.Save(new LauncherSettings { GameDirectory = portableGame, Version = "PCL 实例" });
        Require(File.ReadAllText(store.FilePath).Contains(".minecraft", StringComparison.Ordinal)
            && !File.ReadAllText(store.FilePath).Contains(portableRoot.Replace("\\", "\\\\"), StringComparison.Ordinal), "便携设置应保存相对于启动器的游戏路径。");
        var moved = Directory.CreateDirectory(Path.Combine(root, "portable-new-device")).FullName;
        File.Copy(store.FilePath, Path.Combine(moved, "lattice.portable.json"));
        var movedGame = Directory.CreateDirectory(Path.Combine(moved, ".minecraft", "versions", "PCL 实例")).FullName;
        Require(new SettingsStore(applicationDirectory: moved).Load().GameDirectory == movedGame, "复制到不同目录或设备后应从新的启动器位置解析游戏路径。");
        var wrong = new SettingsStore(applicationDirectory: moved);
        File.WriteAllText(wrong.FilePath, "{\"GameDirectory\":\"Z:/old-device/.minecraft\",\"Version\":\"PCL 实例\"}");
        Require(wrong.Load().GameDirectory == Path.Combine(moved, ".minecraft"), "旧设备绝对路径失效时应自动寻找启动器旁的 .minecraft。");
        File.WriteAllText(wrong.FilePath, "{\"GameDirectory\":null,\"Version\":null,\"OfflineUsername\":null}");
        var recovered = wrong.Load();
        Require(recovered.GameDirectory == Path.Combine(moved, ".minecraft") && recovered.Version == "release" && recovered.OfflineUsername == "Player",
            "便携配置含空值时应恢复可用默认值并识别旁边的游戏目录。");
        File.WriteAllText(wrong.FilePath, "{\"GameDirectory\":\"bad\\u0000path\"}");
        Require(wrong.Load().GameDirectory == Path.Combine(moved, ".minecraft"), "非法目录不能导致读取配置崩溃。");
        wrong.Save(new LauncherSettings { GameDirectory = "bad\0path" });
        Require(wrong.Load().GameDirectory == Path.Combine(moved, ".minecraft"), "用户暂时输入非法路径时自动保存不应崩溃。");
    }

    public static void Inspect(string path)
    {
        var location = MinecraftLocation.Resolve(path);
        var snapshot = LocalContentLibrary.Scan(path);
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
        {
            location.MainDirectory, location.SelectedDirectory, location.SuggestedVersion,
            Files = snapshot.Files.Count,
            ByKind = snapshot.Files.GroupBy(file => file.Kind).ToDictionary(group => group.Key.ToString(), group => group.Count()),
            Instances = LocalContentLibrary.GameDirectories(location).Select(directory => new
            {
                Name = Path.GetFileName(directory),
                GameDirectory = location.GetGameDirectory(Path.GetFileName(directory))
            }),
            Warnings = snapshot.Warnings.Count
        }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }
    private static void Reject(Action action)
    { try { action(); } catch (IOException) { return; } throw new InvalidOperationException("越界或已变更文件必须拒绝删除。"); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
