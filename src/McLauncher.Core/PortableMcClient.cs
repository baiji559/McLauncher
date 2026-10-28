using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace McLauncher.Core;

public sealed class PortableMcClient(string executablePath)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public event EventHandler<PortableMcEvent>? EventReceived;

    public async Task<IReadOnlyList<MinecraftVersion>> GetReleaseVersionsAsync(
        string mainDirectory, CancellationToken cancellationToken)
    {
        var events = await RunAsync(PortableMcArguments.SearchReleases(mainDirectory), cancellationToken, "row");
        return events
            .Where(e => e.Values.Count >= 3
                && e.Values[1].StartsWith("release", StringComparison.Ordinal))
            .Select(e => new MinecraftVersion(
                e.Values[0],
                DateTimeOffset.TryParse(e.Values[2], CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                    ? date
                    : null))
            .ToArray();
    }

    public async Task<IReadOnlyList<MinecraftVersion>> GetLocalVersionsAsync(
        string mainDirectory, CancellationToken cancellationToken)
    {
        var events = await RunAsync(PortableMcArguments.SearchLocal(mainDirectory), cancellationToken, "row");
        return events
            .Where(e => e.Values.Count >= 2 && e.Values[0] != "name")
            .Select(e => new MinecraftVersion(
                e.Values[0],
                DateTimeOffset.TryParse(e.Values[1], CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                    ? date
                    : null))
            .ToArray();
    }

    public async Task<IReadOnlyList<MinecraftAccount>> GetAccountsAsync(
        string gameDirectory, CancellationToken cancellationToken)
    {
        var events = await RunAsync(PortableMcArguments.ListAccounts(gameDirectory), cancellationToken, "row");
        return events
            .Where(e => e.Values.Count >= 2 && e.Values[0] != "username")
            .Select(e => new MinecraftAccount(e.Values[0], e.Values[1]))
            .ToArray();
    }

    public async Task<MinecraftAccount?> LoginMicrosoftAsync(
        string gameDirectory, CancellationToken cancellationToken)
    {
        var events = await RunAsync(PortableMcArguments.LoginMicrosoft(gameDirectory), cancellationToken, "auth_account_authenticated");
        var authenticated = events.LastOrDefault(e => e.Values.Count >= 2);
        return authenticated is null ? null : new MinecraftAccount(authenticated.Values[1], authenticated.Values[0]);
    }

    public Task RepairAsync(
        string mainDirectory, string gameDirectory, string version, int memoryMb,
        CancellationToken cancellationToken) =>
        RunAsync(PortableMcArguments.Repair(mainDirectory, gameDirectory, version, memoryMb), cancellationToken);

    public Task LaunchOfflineAsync(
        string mainDirectory, string gameDirectory, string version, int memoryMb, string username,
        CancellationToken cancellationToken) =>
        RunAsync(PortableMcArguments.LaunchOffline(
            mainDirectory, gameDirectory, version, memoryMb, username), cancellationToken);

    public Task LaunchMicrosoftAsync(
        string mainDirectory, string gameDirectory, string version, int memoryMb, string uuid,
        CancellationToken cancellationToken) =>
        RunAsync(PortableMcArguments.LaunchMicrosoft(
            mainDirectory, gameDirectory, version, memoryMb, uuid), cancellationToken);

    private async Task<IReadOnlyList<PortableMcEvent>> RunAsync(
        IReadOnlyList<string> arguments, CancellationToken cancellationToken, string? resultKind = null)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(executablePath))
                throw new FileNotFoundException("未找到 PortableMC 后端。", executablePath);

            var startInfo = new ProcessStartInfo(executablePath)
            {
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                StandardErrorEncoding = Encoding.UTF8,
                StandardOutputEncoding = Encoding.UTF8,
                UseShellExecute = false
            };
            foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

            var events = new List<PortableMcEvent>();
            PortableMcEvent? lastError = null;
            string? lastStderr = null;
            using var process = new Process { StartInfo = startInfo };
            if (!process.Start()) throw new InvalidOperationException("PortableMC 后端未能启动。");

            using var registration = cancellationToken.Register(() =>
            {
                try
                {
                    if (!process.HasExited) process.Kill(true);
                }
                catch (InvalidOperationException) { }
                catch (Win32Exception) { }
            });

            var stdout = ReadLinesAsync(process.StandardOutput, line =>
            {
                var item = PortableMcEvent.Parse(line);
                if (item.Kind == resultKind) events.Add(item);
                if (item.IsError) lastError = item;
                EventReceived?.Invoke(this, item);
            });
            var stderr = ReadLinesAsync(process.StandardError, line =>
            {
                lastStderr = line;
                EventReceived?.Invoke(this, new PortableMcEvent("stderr", [line]));
            });

            // The cancellation registration terminates the process; drain both pipes before disposing it.
            await process.WaitForExitAsync();
            await Task.WhenAll(stdout, stderr);
            cancellationToken.ThrowIfCancellationRequested();
            if (process.ExitCode != 0)
            {
                var message = lastError is not null ? $"{lastError.Kind}: {string.Join(' ', lastError.Values)}"
                    : lastStderr is not null ? $"stderr: {lastStderr}" : $"PortableMC 退出，代码 {process.ExitCode}。";
                throw new PortableMcException(message, process.ExitCode);
            }
            return events;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static async Task ReadLinesAsync(StreamReader reader, Action<string> onLine)
    {
        while (await reader.ReadLineAsync() is { } line) onLine(line);
    }
}

public sealed class PortableMcException : Exception
{
    internal PortableMcException(string message, int exitCode) : base(message) => ExitCode = exitCode;

    public int ExitCode { get; }
}
