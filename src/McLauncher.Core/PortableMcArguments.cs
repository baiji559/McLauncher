namespace McLauncher.Core;

public static class PortableMcArguments
{
    public static IReadOnlyList<string> SearchReleases(string gameDirectory, int limit = 50) =>
        [.. Base(gameDirectory, "search"), "--channel", "release", "--limit", limit.ToString()];

    public static IReadOnlyList<string> SearchLocal(string mainDirectory) =>
        [.. Base(mainDirectory, "search"), "--kind", "local"];

    public static IReadOnlyList<string> ListAccounts(string gameDirectory) =>
        [.. Base(gameDirectory, "auth"), "list"];

    public static IReadOnlyList<string> LoginMicrosoft(string gameDirectory) =>
        [.. Base(gameDirectory, "auth"), "login", "--no-browser"];

    public static IReadOnlyList<string> Repair(
        string mainDirectory, string gameDirectory, string version, int memoryMb) =>
        [.. Start(mainDirectory, gameDirectory, memoryMb), "--dry", version];

    public static IReadOnlyList<string> LaunchOffline(
        string mainDirectory, string gameDirectory, string version, int memoryMb, string username) =>
        [.. Start(mainDirectory, gameDirectory, memoryMb), "--username", username, version];

    public static IReadOnlyList<string> LaunchMicrosoft(
        string mainDirectory, string gameDirectory, string version, int memoryMb, string uuid) =>
        [.. Start(mainDirectory, gameDirectory, memoryMb), "--auth", "--uuid", uuid, version];

    private static string[] Base(string gameDirectory, string command) =>
        ["--main-dir", Path.GetFullPath(gameDirectory), "--output", "machine", command];

    private static string[] Start(string mainDirectory, string gameDirectory, int memoryMb) =>
        [.. Base(mainDirectory, "start"), "--mc-dir", Path.GetFullPath(gameDirectory),
            "--jvm-policy", "system-then-mojang", $"--jvm-arg=-Xmx{memoryMb}M"];
}

