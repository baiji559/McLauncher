using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows.Input;
using McLauncher.Core;

namespace McLauncher.App;

public enum LauncherPage { Home, Versions, Content, Settings, Logs }

public sealed record LocalVersionEntry(string Id, string Loader)
{
    public string Detail => $"Java 版 · {Loader}";
}

public sealed record InstalledModEntry(string FileName)
{
    public string DisplayName => Path.GetFileNameWithoutExtension(FileName);
}

public sealed partial class MainViewModel
{
    private LauncherPage _currentPage;
    private int _installedModCount;

    public ObservableCollection<LocalVersionEntry> LocalVersions { get; } = [];
    public ObservableCollection<InstalledModEntry> InstalledMods { get; } = [];
    public ICommand OpenGameFolderCommand { get; private set; } = null!;

    public LauncherPage CurrentPage
    {
        get => _currentPage;
        set
        {
            if (!Set(ref _currentPage, value)) return;
            foreach (var property in new[] { nameof(IsHomePage), nameof(IsVersionsPage), nameof(IsContentPage), nameof(IsSettingsPage), nameof(IsLogsPage) }) Notify(property);
        }
    }

    public bool IsHomePage { get => CurrentPage == LauncherPage.Home; set { if (value) CurrentPage = LauncherPage.Home; } }
    public bool IsVersionsPage { get => CurrentPage == LauncherPage.Versions; set { if (value) CurrentPage = LauncherPage.Versions; } }
    public bool IsContentPage { get => CurrentPage == LauncherPage.Content; set { if (value) CurrentPage = LauncherPage.Content; } }
    public bool IsSettingsPage { get => CurrentPage == LauncherPage.Settings; set { if (value) CurrentPage = LauncherPage.Settings; } }
    public bool IsLogsPage { get => CurrentPage == LauncherPage.Logs; set { if (value) CurrentPage = LauncherPage.Logs; } }

    public string SelectedVersionDisplay => SelectedVersion == "release" ? "最新正式版" : SelectedVersion;
    public string AccountDisplayName => UseMicrosoft ? SelectedAccount?.Username ?? "未登录" : string.IsNullOrWhiteSpace(OfflineUsername) ? "Player" : OfflineUsername;
    public string AccountModeLabel => UseMicrosoft ? "微软正版账号" : "离线账号";
    public string MemoryLabel => $"{MemoryMb / 1024d:0.#} GB";
    public string LaunchButtonLabel => IsBusy ? "正在处理…" : "启动游戏";
    public bool HasLocalVersions => LocalVersions.Count > 0;
    public IEnumerable<LocalVersionEntry> RecentVersions => LocalVersions.Take(3);
    public bool HasNoLocalVersions => !HasLocalVersions;
    public string LocalVersionSummary => $"{LocalVersions.Count} 个本地版本";
    public bool HasInstalledMods => _installedModCount > 0;
    public bool HasNoInstalledMods => !HasInstalledMods;
    public string InstalledModSummary => $"{_installedModCount} 个模组";
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
        foreach (var property in new[] { nameof(HasLocalVersions), nameof(HasNoLocalVersions), nameof(LocalVersionSummary), nameof(RecentVersions) }) Notify(property);
        NotifyVersionPresentation();
    }

    private void RefreshInstalledMods()
    {
        InstalledMods.Clear();
        _installedModCount = 0;
        try
        {
            if (!string.IsNullOrWhiteSpace(GameDirectory))
            {
                var location = MinecraftLocation.Resolve(Environment.ExpandEnvironmentVariables(GameDirectory.Trim()));
                var directory = Path.Combine(location.GetGameDirectory(SelectedVersion.Trim()), "mods");
                if (Directory.Exists(directory))
                {
                    var mods = Directory.EnumerateFiles(directory, "*.jar").Select(Path.GetFileName).ToArray();
                    _installedModCount = mods.Length;
                    foreach (var name in mods.Order().Take(3)) InstalledMods.Add(new(name!));
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { }

        Notify(nameof(HasInstalledMods));
        Notify(nameof(HasNoInstalledMods));
        Notify(nameof(InstalledModSummary));
    }

    private void NotifyVersionPresentation()
    {
        Notify(nameof(IsSelectedVersionInstalled));
        Notify(nameof(InstallationLabel));
        Notify(nameof(SelectedLoaderLabel));
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
