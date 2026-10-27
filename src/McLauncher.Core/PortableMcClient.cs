using System.Collections.Concurrent;
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
        var result = await RunAsync(PortableMcArguments.SearchReleases(mainDirectory), cancellationToken);
        return result.Events
            .Where(e => e.Kind == "row" && e.Values.Count >= 3
                && e.Values[1].StartsWith("release", StringComparison.Ordinal))
            .Select(e => new MinecraftVersion(
                e.Values[0],
                e.Values[1].TrimEnd('*'),
                DateTimeOffset.TryParse(e.Values[2], CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                    ? date
                    : null))
            .ToArray();
    }

    public async Task<IReadOnlyList<MinecraftVersion>> GetLocalVersionsAsync(
        string mainDirectory, CancellationToken cancellationToken)
    {
        var result = await RunAsync(PortableMcArguments.SearchLocal(mainDirectory), cancellationToken);
        return result.Events
            .Where(e => e.Kind == "row" && e.Values.Count >= 2 && e.Values[0] != "name")
            .Select(e => new MinecraftVersion(
                e.Values[0],
                "local",
                DateTimeOffset.TryParse(e.Values[1], CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                    ? date
                    : null))
            .ToArray();
    }

    public async Task<IReadOnlyList<MinecraftAccount>> GetAccountsAsync(
        string gameDirectory, CancellationToken cancellationToken)
    {
        var result = await RunAsync(PortableMcArguments.ListAccounts(gameDirectory), cancellationToken);
        return result.Events
            .Where(e => e.Kind == "row" && e.Values.Count >= 2 && e.Values[0] != "username")
            .Select(e => new MinecraftAccount(e.Values[0], e.Values[1]))
            .ToArray();
    }

    public async Task<MinecraftAccount?> LoginMicrosoftAsync(
        string gameDirectory, CancellationToken cancellationToken)
    {
        var result = await RunAsync(PortableMcArguments.LoginMicrosoft(gameDirectory), cancellationToken);
        var authenticated = result.Events.LastOrDefault(e =>
            e.Kind == "auth_account_authenticated" && e.Values.Count >= 2);
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

    private async Task<PortableMcResult> RunAsync(
        IReadOnlyList<string> arguments, CancellationToken cancellationToken)
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

            var events = new ConcurrentQueue<PortableMcEvent>();
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
                events.Enqueue(item);
                EventReceived?.Invoke(this, item);
            });
            var stderr = ReadLinesAsync(process.StandardError, line =>
            {
                var item = new PortableMcEvent("stderr", [line], line);
                events.Enqueue(item);
                EventReceived?.Invoke(this, item);
            });

            try
            {
                await process.WaitForExitAsync(cancellationToken);
                await Task.WhenAll(stdout, stderr);
            }
            catch (OperationCanceledException)
            {
                try { if (!process.HasExited) process.Kill(true); }
                catch (InvalidOperationException) { }
                catch (Win32Exception) { }
                await process.WaitForExitAsync();
                await Task.WhenAll(stdout, stderr);
                throw;
            }
            var result = new PortableMcResult(process.ExitCode, events.ToArray());
            if (result.ExitCode != 0) throw PortableMcException.From(result);
            return result;
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

public sealed record PortableMcResult(int ExitCode, IReadOnlyList<PortableMcEvent> Events);

public sealed class PortableMcException : Exception
{
    private PortableMcException(string message, int exitCode) : base(message) => ExitCode = exitCode;

    public int ExitCode { get; }

    internal static PortableMcException From(PortableMcResult result)
    {
        var errors = result.Events.Where(e => e.IsError || e.Kind == "stderr").ToArray();
        var message = errors.Length == 0
            ? $"PortableMC 退出，代码 {result.ExitCode}。"
            : string.Join(Environment.NewLine, errors.Select(e => $"{e.Kind}: {string.Join(' ', e.Values)}"));
        return new PortableMcException(message, result.ExitCode);
    }
}
