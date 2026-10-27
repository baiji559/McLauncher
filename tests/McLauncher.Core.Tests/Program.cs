using McLauncher.Core;

var tests = new (string Name, Action Run)[]
{
    ("解析机器输出", ParseMachineEvent),
    ("构造离线启动参数", BuildOfflineArguments),
    ("构造微软启动参数", BuildMicrosoftArguments),
    ("构造修复参数", BuildRepairArguments),
    ("构造本地搜索参数", BuildLocalSearchArguments),
    ("识别隔离版本目录", ResolveIsolatedVersionDirectory)
};

foreach (var test in tests)
{
    test.Run();
    Console.WriteLine($"PASS {test.Name}");
}

static void ParseMachineEvent()
{
    var item = PortableMcEvent.Parse("auth_device_code\thttps://microsoft.com/link\tABCD-EFGH");
    Equal("auth_device_code", item.Kind);
    Equal("ABCD-EFGH", item.Values[1]);
    Equal("line one\nline two", PortableMcEvent.DecodeText("line one\\nline two"));
}

static void BuildOfflineArguments()
{
    var args = PortableMcArguments.LaunchOffline(
        "C:\\Games\\.minecraft", "C:\\Games\\Instance", "1.21.5", 4096, "Steve_01");
    ContainsPair(args, "--username", "Steve_01");
    ContainsPair(args, "--mc-dir", Path.GetFullPath("C:\\Games\\Instance"));
    Contains(args, "--jvm-arg=-Xmx4096M");
    Equal("1.21.5", args[^1]);
}

static void BuildMicrosoftArguments()
{
    var args = PortableMcArguments.LaunchMicrosoft(
        "C:\\Games\\.minecraft", "C:\\Games\\Instance", "1.21.5", 8192, "uuid-value");
    Contains(args, "--auth");
    ContainsPair(args, "--uuid", "uuid-value");
    if (args.Contains("--username")) throw new InvalidOperationException("微软启动参数不应含离线用户名。");
}

static void BuildRepairArguments()
{
    var args = PortableMcArguments.Repair(
        "C:\\Games\\.minecraft", "C:\\Games\\Instance", "1.20.1", 2048);
    Contains(args, "--dry");
    Equal("1.20.1", args[^1]);
}

static void BuildLocalSearchArguments()
{
    var args = PortableMcArguments.SearchLocal("C:\\Games\\.minecraft");
    ContainsPair(args, "--kind", "local");
}

static void ResolveIsolatedVersionDirectory()
{
    var root = Directory.CreateTempSubdirectory("McLauncher.Tests.");
    try
    {
        const string version = "Lets go cobblemon master [5.9Beta]";
        var versionDirectory = Path.Combine(root.FullName, "versions", version);
        Directory.CreateDirectory(Path.Combine(versionDirectory, "mods"));
        File.WriteAllText(Path.Combine(versionDirectory, version + ".json"), "{}");

        var direct = MinecraftLocation.Resolve(versionDirectory);
        Equal(root.FullName, direct.MainDirectory);
        Equal(version, direct.SuggestedVersion);
        Equal(versionDirectory, direct.GetGameDirectory(version));

        var fromRoot = MinecraftLocation.Resolve(root.FullName);
        Equal(versionDirectory, fromRoot.GetGameDirectory(version));
    }
    finally
    {
        Directory.Delete(root.FullName, true);
    }
}

static void Contains(IReadOnlyList<string> values, string expected)
{
    if (!values.Contains(expected)) throw new InvalidOperationException($"缺少参数：{expected}");
}

static void ContainsPair(IReadOnlyList<string> values, string name, string expectedValue)
{
    var index = IndexOf(values, name);
    if (index < 0 || index + 1 >= values.Count) throw new InvalidOperationException($"缺少参数：{name}");
    Equal(expectedValue, values[index + 1]);
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"预期 {expected}，实际 {actual}。");
}

static int IndexOf(IReadOnlyList<string> values, string value)
{
    for (var index = 0; index < values.Count; index++)
        if (values[index] == value) return index;
    return -1;
}

