using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows.Input;
using McLauncher.Core;

namespace McLauncher.App;

public enum LauncherPage { Home, Mods, Content, Settings, Instance, Versions, Logs }

public sealed record LocalVersionEntry(string Id, string Loader)
{
    public string Detail => $"Java 版 · {Loader}";
}

public sealed partial class MainViewModel
{
    private LauncherPage _currentPage;
    private string _versionQuery = "";

    public ObservableCollection<LocalVersionEntry> LocalVersions { get; } = [];
    public ObservableCollection<InstalledModEntry> InstalledMods { get; } = [];
    public ICommand OpenGameFolderCommand { get; private set; } = null!;
    public string VersionQuery
    {
        get => _versionQuery;
        set
        {
            if (!Set(ref _versionQuery, value)) return;
            Notify(nameof(FilteredVersions));
            Notify(nameof(HasNoFilteredVersions));
        }
    }
    public IEnumerable<LocalVersionEntry> FilteredVersions => LocalVersions.Where(version =>
        version.Id.Contains(VersionQuery.Trim(), StringComparison.OrdinalIgnoreCase)
        || version.Loader.Contains(VersionQuery.Trim(), StringComparison.OrdinalIgnoreCase));
    public bool HasNoFilteredVersions => !FilteredVersions.Any();

    public LauncherPage CurrentPage
    {
        get => _currentPage;
        set
        {
            if (!Set(ref _currentPage, value)) return;
            Notify(nameof(CurrentPageTitle));
            foreach (var property in new[] { nameof(IsHomePage), nameof(IsModsPage), nameof(IsInstancePage), nameof(IsLaunchSection), nameof(IsVersionsPage), nameof(IsContentPage), nameof(IsSettingsPage), nameof(IsLogsPage), nameof(FooterLabel) }) Notify(property);
        }
    }

    public bool IsHomePage { get => CurrentPage == LauncherPage.Home; set { if (value) CurrentPage = LauncherPage.Home; } }
    public bool IsLaunchSection { get => CurrentPage is LauncherPage.Home or LauncherPage.Instance or LauncherPage.Versions; set { if (value && !IsLaunchSection) CurrentPage = LauncherPage.Home; } }
    public bool IsModsPage { get => CurrentPage == LauncherPage.Mods; set { if (value) CurrentPage = LauncherPage.Mods; } }
    public bool IsInstancePage => CurrentPage == LauncherPage.Instance;
    public bool IsVersionsPage { get => CurrentPage == LauncherPage.Versions; set { if (value) CurrentPage = LauncherPage.Versions; } }
    public bool IsContentPage { get => CurrentPage == LauncherPage.Content; set { if (value) CurrentPage = LauncherPage.Content; } }
    public bool IsSettingsPage { get => CurrentPage == LauncherPage.Settings; set { if (value) CurrentPage = LauncherPage.Settings; } }
    public bool IsLogsPage { get => CurrentPage == LauncherPage.Logs; set { if (value) CurrentPage = LauncherPage.Logs; } }

    public string SelectedVersionDisplay => SelectedVersion == "release" ? "最新正式版" : SelectedVersion;
    public string AccountDisplayName => UseMicrosoft ? SelectedAccount?.Username ?? "未登录" : string.IsNullOrWhiteSpace(OfflineUsername) ? "Player" : OfflineUsername;
    public string AccountModeLabel => UseMicrosoft ? "微软正版账号" : "离线账号";
    public string CurrentPageTitle => CurrentPage switch
    {
        LauncherPage.Mods => "模组管理",
        LauncherPage.Instance => "实例详情",
        LauncherPage.Versions => "游戏版本",
        LauncherPage.Content => "资源中心",
        LauncherPage.Settings => "设置",
        LauncherPage.Logs => "运行日志",
        _ => "开始游戏"
    };
    public string LaunchAvailability => IsGameRunning ? "游戏正在运行；可继续搜索和下载资源。"
        : IsBusy ? "正在处理，完成后可切换版本或修改配置。"
        : UseMicrosoft && SelectedAccount is null ? "启动前，请在账号设置中登录微软账号。"
        : IsSelectedVersionInstalled ? "游戏文件已就绪。" : "首次启动会下载游戏文件。";
    public string MemoryLabel => $"{MemoryMb / 1024d:0.#} GB";
    public string LaunchButtonLabel => IsGameRunning ? "游戏运行中" : IsBusy ? "正在处理…" : "启动游戏";
    public bool HasNoLocalVersions => LocalVersions.Count == 0;
    public bool HasNoInstalledMods => InstalledMods.Count == 0;
    public string InstalledModSummary => $"{InstalledMods.Count} 个模组";
    public bool IsSelectedVersionInstalled => LocalVersions.Any(version => version.Id == SelectedVersion);
    public string InstallationLabel => IsSelectedVersionInstalled ? "已安装" : "首次启动时下载";
    public string SelectedLoaderLabel => LocalVersions.FirstOrDefault(version => version.Id == SelectedVersion)?.Loader ?? "原版";

    private void InitializePresentationCommands() => OpenGameFolderCommand = new RelayCommand(() =>
    {
        try
        {
            var location = ValidateLocation();
            var directory = location.GetGameDirectory(SelectedVersion.Trim());
            Directory.CreateDirectory(directory);
            Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            AppendLog($"无法打开游戏目录：{exception.Message}");
            Status = "无法打开目录，详情见日志";
        }
    });

    private void NotifyAccountPresentation()
    {
        Notify(nameof(AccountDisplayName));
        Notify(nameof(AccountModeLabel));
        Notify(nameof(LaunchAvailability));
    }

    public void RefreshDashboard()
    {
        LocalVersions.Clear();
        try
        {
            if (!string.IsNullOrWhiteSpace(GameDirectory))
            {
                var location = MinecraftLocation.Resolve(Environment.ExpandEnvironmentVariables(GameDirectory.Trim()));
                var versionsDirectory = Path.Combine(location.MainDirectory, "versions");
                if (Directory.Exists(versionsDirectory))
                {
                    foreach (var directory in Directory.EnumerateDirectories(versionsDirectory)
                                 .OrderByDescending(Directory.GetLastWriteTimeUtc))
                    {
                        var id = Path.GetFileName(directory);
                        var manifest = Path.Combine(directory, id + ".json");
                        if (!File.Exists(manifest)) continue;
                        LocalVersions.Add(new(id, DetectLoader(id, manifest)));
                    }
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // An inaccessible or partially typed path is presented as an empty local library.
        }

        RefreshInstalledMods();
        Notify(nameof(HasNoLocalVersions));
        Notify(nameof(FilteredVersions));
        Notify(nameof(HasNoFilteredVersions));
        NotifyVersionPresentation();
    }

    private void RefreshInstalledMods()
    {
        InstalledMods.Clear();
        try
        {
            if (!string.IsNullOrWhiteSpace(GameDirectory))
            {
                var location = MinecraftLocation.Resolve(Environment.ExpandEnvironmentVariables(GameDirectory.Trim()));
                var directory = Path.Combine(location.GetGameDirectory(SelectedVersion.Trim()), "mods");
                if (Directory.Exists(directory))
                {
                    var mods = Directory.EnumerateFiles(directory).Where(path => path.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase));
                    foreach (var path in mods.Order()) InstalledMods.Add(new InstalledModEntry(path, OnModChanged));
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { }

        Notify(nameof(HasNoInstalledMods));
        Notify(nameof(InstalledModSummary));
        NotifyModPresentation();
    }

    private void NotifyVersionPresentation()
    {
        NotifySoftVoxelPresentation();
        Notify(nameof(IsSelectedVersionInstalled));
        Notify(nameof(InstallationLabel));
        Notify(nameof(SelectedLoaderLabel));
        Notify(nameof(LaunchAvailability));
    }

    private static string DetectLoader(string id, string manifest)
    {
        try
        {
            // Loader names may only appear in the manifest when a user renames a local version.
            using var json = JsonDocument.Parse(File.ReadAllText(manifest));
            if (json.RootElement.TryGetProperty("libraries", out var libraries) && libraries.ValueKind == JsonValueKind.Array)
                id += " " + string.Join(' ', libraries.EnumerateArray().Where(library => library.ValueKind == JsonValueKind.Object)
                    .Select(library => library.TryGetProperty("name", out var name) ? name.GetString() : ""));
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException) { }

        if (id.Contains("neoforge", StringComparison.OrdinalIgnoreCase)) return "NeoForge";
        if (id.Contains("fabric", StringComparison.OrdinalIgnoreCase)) return "Fabric";
        if (id.Contains("quilt", StringComparison.OrdinalIgnoreCase)) return "Quilt";
        if (id.Contains("forge", StringComparison.OrdinalIgnoreCase)) return "Forge";
        if (id.Contains("optifine", StringComparison.OrdinalIgnoreCase)) return "OptiFine";
        return "原版";
    }
}
