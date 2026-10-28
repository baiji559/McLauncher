using McLauncher.Core;

internal static class PortableMcTests
{
    public static void ProcessOutput() => ProcessOutputAsync().GetAwaiter().GetResult();

    private static async Task ProcessOutputAsync()
    {
        var client = new PortableMcClient(Environment.ProcessPath!);
        var directory = Environment.CurrentDirectory;
        var local = await client.GetLocalVersionsAsync(directory, default);
        Require(local.Count == 1 && local[0].Id == "1.21.1", "本地版本结果丢失。");
        var releases = await client.GetReleaseVersionsAsync(directory, default);
        Require(releases.Count == 1 && releases[0].Id == "1.21.1", "正式版结果丢失。");
        var accounts = await client.GetAccountsAsync(directory, default);
        Require(accounts.Count == 1 && accounts[0].Username == "Alex", "账号结果丢失。");
        var account = await client.LoginMicrosoftAsync(directory, default);
        Require(account?.Uuid == "test-uuid", "登录结果丢失。");

        WeakReference? firstLog = null;
        var logCount = 0;
        void ObserveOutput(object? sender, PortableMcEvent item)
        {
            if (item.Kind == "log_raw")
            {
                firstLog ??= new WeakReference(item);
                logCount++;
            }
            if (item.Kind != "output_complete") return;
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Require(firstLog is { IsAlive: false }, "运行期间不应持有已交付的历史日志。");
        }
        client.EventReceived += ObserveOutput;
        await client.LaunchOfflineAsync(directory, directory, "stream", 1024, "Alex", default);
        client.EventReceived -= ObserveOutput;
        Require(logCount == 10000, "实时日志交付不完整。");

        try
        {
            await client.RepairAsync(directory, directory, "error", 1024, default);
            throw new InvalidOperationException("后端失败应抛出异常。");
        }
        catch (PortableMcException exception)
        {
            Require(exception.ExitCode == 7 && exception.Message.Contains("fixture failure", StringComparison.Ordinal),
                "错误应保留后端退出码和具体原因。");
        }

        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        client.EventReceived += (_, item) => { if (item.Kind == "ready") cancellation.Cancel(); };
        try
        {
            await client.LaunchOfflineAsync(directory, directory, "cancel", 1024, "Alex", cancellation.Token);
            throw new InvalidOperationException("取消后应结束操作。");
        }
        catch (OperationCanceledException) { }
        Require((await client.GetLocalVersionsAsync(directory, default)).Count == 1, "取消后应释放命令锁。");
    }

    // Exercise the real process and pipe handling with deterministic machine output, without starting Minecraft.
    public static async Task RunBackendFixtureAsync(string[] arguments)
    {
        if (arguments.Contains("search"))
        {
            Console.WriteLine(arguments.Contains("local")
                ? "row\tname\tlast_modified\nrow\t1.21.1\t2025-01-01T00:00:00Z"
                : "row\t1.21.1\trelease\t2025-01-01T00:00:00Z");
        }
        else if (arguments.Contains("auth"))
        {
            Console.WriteLine(arguments.Contains("list")
                ? "row\tusername\tuuid\nrow\tAlex\ttest-uuid"
                : "auth_account_authenticated\ttest-uuid\tAlex");
        }
        else if (arguments[^1] == "error")
        {
            Console.Error.WriteLine("fixture stderr");
            Console.WriteLine("error_test\tfixture failure");
            Environment.ExitCode = 7;
        }
        else if (arguments[^1] == "cancel")
        {
            Console.WriteLine("ready");
            await Task.Delay(Timeout.InfiniteTimeSpan);
        }
        else
        {
            for (var i = 0; i < 10000; i++) Console.WriteLine($"log_raw\t{i}: {new string('x', 100)}");
            Console.WriteLine("output_complete");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
