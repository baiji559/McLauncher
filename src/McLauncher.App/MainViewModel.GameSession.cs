namespace McLauncher.App;

public sealed partial class MainViewModel
{
    private CancellationTokenSource? _gameOperation;
    private TaskCompletionSource? _gameStarted;
    private bool _isGameRunning;
    public bool IsGameRunning
    {
        get => _isGameRunning;
        private set
        {
            if (!Set(ref _isGameRunning, value)) return;
            NotifyCommands(RefreshCommand, LoginMicrosoftCommand, RepairCommand, LaunchCommand);
            Notify(nameof(LaunchButtonLabel));
            Notify(nameof(LaunchAvailability));
        }
    }

    private async Task MonitorGameAsync(string mainDirectory, string gameDirectory, string version,
        string identity, bool microsoft, CancellationTokenSource operation)
    {
        try
        {
            if (microsoft)
                await _client.LaunchMicrosoftAsync(mainDirectory, gameDirectory, version, MemoryMb, identity, operation.Token);
            else
                await _client.LaunchOfflineAsync(mainDirectory, gameDirectory, version, MemoryMb, identity, operation.Token);
            if (!IsBusy) Status = "游戏已退出";
        }
        catch (Exception exception)
        {
            // Before launch, the foreground operation owns error/cancellation feedback.
            if (_gameStarted?.Task.IsCompleted != true) throw;
            if (!IsBusy) Status = exception is OperationCanceledException ? "游戏运行已结束" : "游戏异常退出，详情见日志";
            AppendLog(exception.Message);
        }
        finally
        {
            IsGameRunning = false;
            if (ReferenceEquals(_gameOperation, operation)) _gameOperation = null;
            NotifyCommands(RefreshCommand, LoginMicrosoftCommand, RepairCommand, LaunchCommand);
            operation.Dispose();
            RefreshDashboard();
        }
    }
}
