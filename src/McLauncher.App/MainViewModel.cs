using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using McLauncher.Core;

namespace McLauncher.App;

public sealed partial class MainViewModel : ViewModelBase, IDisposable
{
    private readonly PortableMcClient _client;
    private readonly SettingsStore _settingsStore;
    private readonly Dispatcher _dispatcher;
    private CancellationTokenSource? _operation;
    private string _gameDirectory = "";
    private string _selectedVersion = "release";
    private MinecraftAccount? _selectedAccount;
    private string _offlineUsername = "Player";
    private string _status = "准备就绪";
    private string _javaStatus = "将在安装或启动时检测";
    private string _deviceCode = "";
    private int _memoryMb = 4096;
    private double _progress;
    private bool _isProgressIndeterminate;
    private bool _isBusy;
    private bool _useMicrosoft = true;

    public MainViewModel(string portableMcPath)
    {
        _client = new PortableMcClient(portableMcPath);
        _client.EventReceived += OnPortableMcEvent;
        _settingsStore = new SettingsStore();
        _dispatcher = Application.Current.Dispatcher;

        RefreshCommand = new AsyncCommand(() => RunAsync("刷新版本和账号", RefreshAsync), () => !IsBusy);
        LoginMicrosoftCommand = new AsyncCommand(
            () => RunAsync("微软登录", LoginMicrosoftAsync), () => !IsBusy && UseMicrosoft);
        RepairCommand = new AsyncCommand(() => RunAsync("下载或修复", RepairAsync), () => !IsBusy);
        LaunchCommand = new AsyncCommand(() => RunAsync("启动游戏", LaunchAsync), () => !IsBusy);
        CancelCommand = new RelayCommand(Cancel, () => IsBusy);
        ClearLogsCommand = new RelayCommand(Logs.Clear);
        InitializeContentCommands();
    }

    public ObservableCollection<string> Versions { get; } = ["release"];
    public ObservableCollection<MinecraftAccount> Accounts { get; } = [];
    public ObservableCollection<string> Logs { get; } = [];

    public ICommand RefreshCommand { get; }
    public ICommand LoginMicrosoftCommand { get; }
    public ICommand RepairCommand { get; }
    public ICommand LaunchCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand ClearLogsCommand { get; }

    public string GameDirectory
    {
        get => _gameDirectory;
        set { if (Set(ref _gameDirectory, value)) Notify(nameof(ContentDestination)); }
    }

    public string SelectedVersion
    {
        get => _selectedVersion;
        set { if (Set(ref _selectedVersion, value)) Notify(nameof(ContentDestination)); }
    }

    public MinecraftAccount? SelectedAccount
    {
        get => _selectedAccount;
        set => Set(ref _selectedAccount, value);
    }

    public string OfflineUsername
    {
        get => _offlineUsername;
        set => Set(ref _offlineUsername, value);
    }

    public int MemoryMb
    {
        get => _memoryMb;
        set => Set(ref _memoryMb, value);
    }

    public string Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    public string JavaStatus
    {
        get => _javaStatus;
        private set => Set(ref _javaStatus, value);
    }

    public string DeviceCode
    {
        get => _deviceCode;
        private set => Set(ref _deviceCode, value);
    }

    public double Progress
    {
        get => _progress;
        private set => Set(ref _progress, value);
    }

    public bool IsProgressIndeterminate
    {
        get => _isProgressIndeterminate;
        private set => Set(ref _isProgressIndeterminate, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!Set(ref _isBusy, value)) return;
            ((AsyncCommand)RefreshCommand).NotifyCanExecuteChanged();
            ((AsyncCommand)LoginMicrosoftCommand).NotifyCanExecuteChanged();
            ((AsyncCommand)RepairCommand).NotifyCanExecuteChanged();
            ((AsyncCommand)LaunchCommand).NotifyCanExecuteChanged();
            ((RelayCommand)CancelCommand).NotifyCanExecuteChanged();
            NotifyContentCommands();
            Notify(nameof(CanEditContent));
        }
    }

    public bool UseMicrosoft
    {
        get => _useMicrosoft;
        set
        {
            if (!Set(ref _useMicrosoft, value)) return;
            Notify(nameof(UseOffline));
            ((AsyncCommand)LoginMicrosoftCommand).NotifyCanExecuteChanged();
        }
    }

    public bool UseOffline
    {
        get => !UseMicrosoft;
        set { if (value) UseMicrosoft = false; }
    }

    public async Task InitializeAsync()
    {
        var settings = _settingsStore.Load();
        GameDirectory = settings.GameDirectory;
        SelectedVersion = settings.Version;
        MemoryMb = Math.Clamp(settings.MemoryMb, 1024, 32768);
        UseMicrosoft = settings.UseMicrosoftAccount;
        OfflineUsername = settings.OfflineUsername;
        ContentUseSystemProxy = settings.ContentUseSystemProxy;

        await RunAsync("初始化", async cancellationToken =>
        {
            await LoadAccountsAsync(settings.MicrosoftAccountUuid, cancellationToken);
            await LoadVersionsAsync(cancellationToken);
            Status = "准备就绪";
        });
    }

    public void SaveSettings() => _settingsStore.Save(new LauncherSettings
    {
        GameDirectory = GameDirectory,
        Version = SelectedVersion,
        MemoryMb = MemoryMb,
        UseMicrosoftAccount = UseMicrosoft,
        OfflineUsername = OfflineUsername,
        MicrosoftAccountUuid = SelectedAccount?.Uuid,
        ContentUseSystemProxy = ContentUseSystemProxy
    });

    public void Dispose()
    {
        _client.EventReceived -= OnPortableMcEvent;
        _operation?.Cancel();
        _operation?.Dispose();
        _contentHttp.Dispose();
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        var selectedUuid = SelectedAccount?.Uuid;
        await LoadAccountsAsync(selectedUuid, cancellationToken);
        await LoadVersionsAsync(cancellationToken);
        Status = "版本和账号已刷新";
    }

    private async Task LoginMicrosoftAsync(CancellationToken cancellationToken)
    {
        DeviceCode = "";
        var account = await _client.LoginMicrosoftAsync(ValidateLocation().MainDirectory, cancellationToken);
        await LoadAccountsAsync(account?.Uuid, cancellationToken);
        Status = "微软账号登录完成";
    }

    private async Task RepairAsync(CancellationToken cancellationToken)
    {
        var location = ValidateLocation();
        var version = ValidateVersion();
        var gameDirectory = location.GetGameDirectory(version);
        SaveSettings();
        AppendLog($"资源目录：{location.MainDirectory}；运行目录：{gameDirectory}");
        await _client.RepairAsync(
            location.MainDirectory, gameDirectory, version, MemoryMb, cancellationToken);
        Progress = 100;
        Status = $"{version} 下载或修复完成";
    }

    private async Task LaunchAsync(CancellationToken cancellationToken)
    {
        var location = ValidateLocation();
        var version = ValidateVersion();
        var gameDirectory = location.GetGameDirectory(version);
        SaveSettings();
        AppendLog($"资源目录：{location.MainDirectory}；运行目录：{gameDirectory}");

        if (UseMicrosoft)
        {
            if (SelectedAccount is null) throw new InvalidOperationException("请先登录并选择微软账号。");
            await _client.LaunchMicrosoftAsync(
                location.MainDirectory, gameDirectory, version, MemoryMb, SelectedAccount.Uuid, cancellationToken);
        }
        else
        {
            var username = OfflineUsername.Trim();
            if (username.Length is < 3 or > 16 || username.Any(ch => !char.IsAsciiLetterOrDigit(ch) && ch != '_'))
                throw new InvalidOperationException("离线用户名需为 3–16 位英文字母、数字或下划线。");
            await _client.LaunchOfflineAsync(
                location.MainDirectory, gameDirectory, version, MemoryMb, username, cancellationToken);
        }

        Status = "游戏已退出";
    }

    private async Task LoadVersionsAsync(CancellationToken cancellationToken)
    {
        var location = ValidateLocation();
        var selected = location.SuggestedVersion ?? SelectedVersion;
        var local = await _client.GetLocalVersionsAsync(location.MainDirectory, cancellationToken);
        IReadOnlyList<MinecraftVersion> releases = [];

        try
        {
            releases = await _client.GetReleaseVersionsAsync(location.MainDirectory, cancellationToken);
        }
        catch (PortableMcException exception)
        {
            AppendLog($"在线版本列表获取失败，本地版本仍可使用：{exception.Message}");
        }

        Versions.Clear();
        foreach (var version in local.OrderByDescending(version => version.ReleasedAt)) Versions.Add(version.Id);
        foreach (var version in releases)
            if (!Versions.Contains(version.Id)) Versions.Add(version.Id);
        if (location.SuggestedVersion is { } suggested && !Versions.Contains(suggested)) Versions.Insert(0, suggested);
        if (Versions.Count == 0) Versions.Add("release");
        SelectedVersion = string.IsNullOrWhiteSpace(selected) ? Versions[0] : selected;
        if (local.Count > 0) AppendLog($"已识别 {local.Count} 个本地版本");
    }

    private async Task LoadAccountsAsync(string? preferredUuid, CancellationToken cancellationToken)
    {
        var accounts = await _client.GetAccountsAsync(ValidateLocation().MainDirectory, cancellationToken);
        Accounts.Clear();
        foreach (var account in accounts) Accounts.Add(account);
        SelectedAccount = Accounts.FirstOrDefault(account => account.Uuid == preferredUuid) ?? Accounts.FirstOrDefault();
    }

    private async Task RunAsync(string operationName, Func<CancellationToken, Task> action)
    {
        if (IsBusy) return;
        _operation = new CancellationTokenSource();
        IsBusy = true;
        IsProgressIndeterminate = true;
        Progress = 0;
        Status = operationName + "…";
        AppendLog(operationName);

        try
        {
            await action(_operation.Token);
        }
        catch (OperationCanceledException)
        {
            Status = "操作已取消";
            AppendLog("操作已取消");
        }
        catch (Exception exception)
        {
            Status = "操作失败，详情见日志";
            AppendLog(exception.Message);
        }
        finally
        {
            _operation.Dispose();
            _operation = null;
            IsProgressIndeterminate = false;
            IsBusy = false;
        }
    }

    private MinecraftLocation ValidateLocation()
    {
        if (string.IsNullOrWhiteSpace(GameDirectory)) throw new InvalidOperationException("请选择游戏目录。");
        var path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(GameDirectory.Trim()));
        Directory.CreateDirectory(path);
        var location = MinecraftLocation.Resolve(path);
        GameDirectory = location.SelectedDirectory;
        return location;
    }

    private string ValidateVersion() => string.IsNullOrWhiteSpace(SelectedVersion)
        ? throw new InvalidOperationException("请选择游戏版本。")
        : SelectedVersion.Trim();

    private void Cancel() => _operation?.Cancel();

    private void OnPortableMcEvent(object? sender, PortableMcEvent item) =>
        _dispatcher.BeginInvoke(() => HandlePortableMcEvent(item));

    private void HandlePortableMcEvent(PortableMcEvent item)
    {
        switch (item.Kind)
        {
            case "row":
            case "sep":
                return;
            case "download":
                IsProgressIndeterminate = false;
                Progress = ParseProgress(item.Values.ElementAtOrDefault(1));
                Status = $"正在下载 {item.Values.ElementAtOrDefault(0)}";
                return;
            case "auth_device_code" when item.Values.Count >= 2:
                ShowDeviceCode(item.Values[0], item.Values[1]);
                return;
            case "found_jvm_system_version" when item.Values.Count >= 2:
                JavaStatus = $"系统 Java {item.Values[1]}";
                break;
            case "loaded_jvm" when item.Values.Count >= 1:
                JavaStatus = $"已选择 {item.Values[0]}";
                break;
            case "launched" when item.Values.Count >= 1:
                Status = $"游戏运行中，PID {item.Values[0]}";
                break;
            case "terminated" when item.Values.Count >= 1:
                Status = $"游戏已退出，代码 {item.Values[0]}";
                break;
        }

        AppendLog(FormatEvent(item));
    }

    private void ShowDeviceCode(string verificationUri, string code)
    {
        if (DeviceCode == code) return;
        DeviceCode = code;
        Status = $"请在微软页面输入代码 {code}";
        try
        {
            Clipboard.SetText(code);
            AppendLog($"登录代码 {code} 已复制");
        }
        catch (Exception exception)
        {
            AppendLog($"复制登录代码失败：{exception.Message}");
        }

        if (!Uri.TryCreate(verificationUri, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            AppendLog($"登录地址无效：{verificationUri}");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            AppendLog("微软验证页面已打开");
        }
        catch (Exception exception)
        {
            AppendLog($"请手动打开 {uri.AbsoluteUri} 并输入 {code}。{exception.Message}");
        }
    }

    private static double ParseProgress(string? value)
    {
        var parts = value?.Split('/', 2);
        if (parts is not { Length: 2 }
            || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var current)
            || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var total)
            || total <= 0) return 0;
        return Math.Clamp(current / total * 100, 0, 100);
    }

    private static string FormatEvent(PortableMcEvent item) => item.Kind switch
    {
        "log_raw" => PortableMcEvent.DecodeText(item.Values.ElementAtOrDefault(0) ?? ""),
        "log_xml" => $"[{item.Values.ElementAtOrDefault(1)}] [{item.Values.ElementAtOrDefault(3)}] " +
                     $"[{item.Values.ElementAtOrDefault(0)}] " +
                     PortableMcEvent.DecodeText(item.Values.ElementAtOrDefault(4) ?? ""),
        "stderr" => item.Values.ElementAtOrDefault(0) ?? "",
        _ => $"{item.Kind}: {string.Join(" | ", item.Values)}"
    };

    private void AppendLog(string message)
    {
        Logs.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
        while (Logs.Count > 1000) Logs.RemoveAt(0);
    }
}

