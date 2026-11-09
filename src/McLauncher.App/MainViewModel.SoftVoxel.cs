using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using McLauncher.Core;

namespace McLauncher.App;

public sealed class InstalledModEntry(string path, Action<string> changed) : ViewModelBase
{
    private string _path = path;
    public string FileName => Path.GetFileName(_path);
    public string DisplayName => Path.GetFileNameWithoutExtension(FileName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase) ? Path.GetFileNameWithoutExtension(FileName) : FileName);
    public string Description => IsEnabled ? "已启用 · 本地模组" : "已停用 · 文件保留在当前实例";
    public string ProjectName => DisplayName.Split('-')[0];
    public string FileSize => $"{new FileInfo(_path).Length / 1024d:0.#} KB";
    public bool IsEnabled
    {
        get => !_path.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
        set
        {
            if (IsEnabled == value) return;
            try
            {
                var destination = value ? _path[..^9] : _path + ".disabled";
                File.Move(_path, destination, false);
                _path = destination;
                Notify(); Notify(nameof(FileName)); Notify(nameof(Description));
                changed("");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Notify(); changed($"无法切换模组：{exception.Message}");
            }
        }
    }
}

public sealed partial class MainViewModel
{
    private string _modQuery = "";
    private bool _enabledModsOnly;
    public string ModQuery { get => _modQuery; set { if (Set(ref _modQuery, value)) NotifyModPresentation(); } }
    public bool EnabledModsOnly { get => _enabledModsOnly; set { if (Set(ref _enabledModsOnly, value)) NotifyModPresentation(); } }
    public IEnumerable<InstalledModEntry> FilteredMods => InstalledMods.Where(mod => (!EnabledModsOnly || mod.IsEnabled) && mod.DisplayName.Contains(ModQuery.Trim(), StringComparison.OrdinalIgnoreCase));
    public bool HasNoFilteredMods => !FilteredMods.Any();
    public string ModCountLabel => $"全部 {InstalledMods.Count}";
    public string ModDescription => $"{InstanceDisplayName} · {InstalledMods.Count} 个模组 · 所有修改仅作用于当前实例。";
    public string EnabledModLabel => $"已启用 {InstalledMods.Count(mod => mod.IsEnabled)}";
    public string InstanceDisplayName => SelectedVersion == "release" ? "Minecraft Java Edition" : IsVersionNumber(SelectedVersion) ? "Minecraft" : SelectedVersion;
    public string GameVersionDisplay
    {
        get
        {
            try
            {
                var location = MinecraftLocation.Resolve(GameDirectory);
                var manifest = Path.Combine(location.MainDirectory, "versions", SelectedVersion, SelectedVersion + ".json");
                if (File.Exists(manifest))
                {
                    using var document = JsonDocument.Parse(File.ReadAllText(manifest));
                    if (document.RootElement.TryGetProperty("inheritsFrom", out var parent)) return parent.GetString() ?? SelectedVersionDisplay;
                }
            }
            catch (Exception e) when (e is IOException or JsonException or ArgumentException or UnauthorizedAccessException) { }
            return IsVersionNumber(SelectedVersion) ? SelectedVersion : SelectedVersion == "release" ? "最新版" : "Java 版";
        }
    }
    public string InstanceSubtitle => SelectedLoaderLabel == "原版" ? "原汁原味，无限可能。" : $"{SelectedLoaderLabel} · 你的专属模组世界";
    public string InstanceDescription => $"{InstanceDisplayName}，探索、建筑与冒险从这里开始。管理当前实例的模组、资源和运行环境。";
    public string InstanceDirectory
    {
        get { try { return MinecraftLocation.Resolve(GameDirectory).GetGameDirectory(SelectedVersion); } catch (Exception e) when (e is ArgumentException or NotSupportedException) { return GameDirectory; } }
    }
    public string FooterLabel => CurrentPage switch { LauncherPage.Mods => $"{InstalledMods.Count} MODS  /  {InstalledMods.Count(mod => mod.IsEnabled)} ENABLED", LauncherPage.Content => "DISCOVER  /  SAFE DOWNLOADS", LauncherPage.Settings => "PREFERENCES  /  LOCAL", LauncherPage.Instance => "INSTANCE  /  LOCAL FILES", _ => "LATTICE  /  MINECRAFT JAVA EDITION" };
    private static bool IsVersionNumber(string value) => value.Length > 0 && char.IsDigit(value[0]) && value.All(c => char.IsDigit(c) || c == '.');
    private void NotifyModPresentation()
    {
        foreach (var name in new[] { nameof(FilteredMods), nameof(HasNoFilteredMods), nameof(ModCountLabel), nameof(ModDescription), nameof(EnabledModLabel), nameof(FooterLabel) }) Notify(name);
    }
    private void OnModChanged(string error)
    {
        if (error.Length > 0) { Status = error; AppendLog(error); }
        NotifyModPresentation();
    }
    private void NotifySoftVoxelPresentation()
    {
        foreach (var name in new[] { nameof(InstanceDisplayName), nameof(GameVersionDisplay), nameof(InstanceSubtitle), nameof(InstanceDescription), nameof(InstanceDirectory), nameof(ModDescription) }) Notify(name);
    }
}
