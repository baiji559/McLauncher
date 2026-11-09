using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using McLauncher.Core;
using Microsoft.VisualBasic.FileIO;

namespace McLauncher.App;

public sealed partial class MainViewModel
{
    private int _localScanVersion;
    private bool _isLocalScanBusy;
    private bool _isLocalDeleting;
    private string _localDownloadQuery = "";
    private ContentKind? _localDownloadKind;
    private string _localDownloadMessage = "查看游戏目录及全部实例中的本地资源。";
    private LocalContentFile? _pendingLocalDeletion;
    private Action<string> _recycleLocalFile = RecycleLocalFile;

    private static void RecycleLocalFile(string path)
    {
        // Report a locked file in the application before the shell can open an error dialog.
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None)) { }
        FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin, UICancelOption.ThrowException);
        if (File.Exists(path)) throw new IOException("Windows 未移除这个文件，请关闭占用程序后重试。");
    }

    public ObservableCollection<LocalContentFile> LocalDownloads { get; } = [];
    public IReadOnlyList<ContentChoice<ContentKind?>> LocalDownloadKinds { get; } =
        [new(null, "全部类型"), new(ContentKind.Mod, "模组"), new(ContentKind.Modpack, "整合包"), new(ContentKind.ResourcePack, "资源包"), new(ContentKind.Shader, "光影")];
    public ICommand RefreshLocalDownloadsCommand { get; private set; } = null!;
    public ICommand ConfirmLocalDeletionCommand { get; private set; } = null!;
    public ICommand CancelLocalDeletionCommand { get; private set; } = null!;
    public bool IsLocalScanBusy { get => _isLocalScanBusy; private set { if (Set(ref _isLocalScanBusy, value)) NotifyLocalCommands(); } }
    public bool IsLocalDeleting { get => _isLocalDeleting; private set { if (Set(ref _isLocalDeleting, value)) NotifyLocalCommands(); } }
    public bool CanManageLocalFiles => !IsLocalScanBusy && !IsLocalDeleting;
    public string LocalDownloadQuery { get => _localDownloadQuery; set { if (Set(ref _localDownloadQuery, value)) NotifyLocalDownloads(); } }
    public ContentKind? LocalDownloadKind { get => _localDownloadKind; set { if (Set(ref _localDownloadKind, value)) NotifyLocalDownloads(); } }
    public string LocalDownloadMessage { get => _localDownloadMessage; private set => Set(ref _localDownloadMessage, value); }
    public string LocalDownloadScope
    {
        get { try { return MinecraftLocation.Resolve(GameDirectory).MainDirectory; } catch (Exception e) when (e is ArgumentException or NotSupportedException) { return GameDirectory; } }
    }
    public IEnumerable<LocalContentFile> FilteredLocalDownloads => LocalDownloads.Where(file =>
        (LocalDownloadKind is null || file.Kind == LocalDownloadKind)
        && (file.FileName.Contains(LocalDownloadQuery.Trim(), StringComparison.OrdinalIgnoreCase)
            || file.InstanceName.Contains(LocalDownloadQuery.Trim(), StringComparison.OrdinalIgnoreCase)));
    public bool HasNoLocalDownloads => !IsLocalScanBusy && !FilteredLocalDownloads.Any();
    public string LocalDownloadSummary => $"显示 {FilteredLocalDownloads.Count()} / {LocalDownloads.Count} 个文件 · 共 {LocalContentFile.FormatSize(LocalDownloads.Sum(file => file.Size))}";
    public LocalContentFile? PendingLocalDeletion
    {
        get => _pendingLocalDeletion;
        private set { if (Set(ref _pendingLocalDeletion, value)) { Notify(nameof(HasPendingLocalDeletion)); NotifyLocalCommands(); } }
    }
    public bool HasPendingLocalDeletion => PendingLocalDeletion is not null;

    private void InitializeLocalDownloadCommands()
    {
        RefreshLocalDownloadsCommand = new AsyncCommand(RefreshLocalDownloadsAsync, () => !IsLocalScanBusy && !IsLocalDeleting);
        ConfirmLocalDeletionCommand = new AsyncCommand(DeleteLocalDownloadAsync, () => CanManageLocalFiles && PendingLocalDeletion is not null);
        CancelLocalDeletionCommand = new RelayCommand(CancelLocalDeletion, () => !IsLocalDeleting);
    }
    public async Task RefreshLocalDownloadsAsync()
    {
        var scan = ++_localScanVersion;
        var directory = GameDirectory;
        IsLocalScanBusy = true;
        LocalDownloadMessage = "正在扫描本地资源…";
        try
        {
            var result = await Task.Run(() => LocalContentLibrary.Scan(directory));
            if (scan != _localScanVersion || directory != GameDirectory) return;
            LocalDownloads.Clear();
            foreach (var file in result.Files) LocalDownloads.Add(file);
            if (PendingLocalDeletion is { } pending && !LocalDownloads.Contains(pending)) PendingLocalDeletion = null;
            LocalDownloadMessage = result.Warnings.Count == 0 ? "已读取共享游戏目录及全部实例；也包含手动放入的资源文件。"
                : $"已读取可访问的资源；{result.Warnings.Count} 项无法读取，详情见日志。";
            foreach (var warning in result.Warnings) AppendLog("本地资源：" + warning);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            if (scan != _localScanVersion) return;
            LocalDownloads.Clear();
            LocalDownloadMessage = $"无法读取本地资源：{e.Message} 请检查游戏目录后刷新。";
        }
        finally
        {
            if (scan == _localScanVersion) { IsLocalScanBusy = false; NotifyLocalDownloads(); }
        }
    }
    private void InvalidateLocalDownloads()
    {
        ++_localScanVersion;
        LocalDownloads.Clear(); PendingLocalDeletion = null; IsLocalScanBusy = false;
        Notify(nameof(LocalDownloadScope)); NotifyLocalDownloads();
    }
    public void RequestLocalDeletion(LocalContentFile file)
    {
        if (!CanManageLocalFiles || !LocalDownloads.Contains(file))
        {
            LocalDownloadMessage = "本地资源正在刷新或删除，请稍候再试。";
            return;
        }
        PendingLocalDeletion = file;
        LocalDownloadMessage = $"请在列表上方确认删除：{file.FileName}";
        Status = LocalDownloadMessage;
    }
    private void CancelLocalDeletion()
    {
        if (IsLocalDeleting || PendingLocalDeletion is null) return;
        PendingLocalDeletion = null;
        LocalDownloadMessage = "已取消删除，文件保留。";
        Status = LocalDownloadMessage;
    }
    private async Task DeleteLocalDownloadAsync()
    {
        if (!CanManageLocalFiles || PendingLocalDeletion is not { } file || !LocalDownloads.Contains(file)) return;
        var directory = GameDirectory;
        IsLocalDeleting = true;
        LocalDownloadMessage = $"正在移到回收站：{file.FileName}…";
        try
        {
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Render);
            if (directory != GameDirectory || PendingLocalDeletion != file) return;
            var path = LocalContentLibrary.ValidateFile(directory, file);
            _recycleLocalFile(path);
            AppendLog($"已移到回收站：{path}");
            PendingLocalDeletion = null;
            await RefreshLocalDownloadsAsync();
            if (directory != GameDirectory) return;
            RefreshInstalledMods();
            Notify(nameof(ContentSelectionHint));
            LocalDownloadMessage = $"已移到回收站：{file.FileName}。可在 Windows 回收站还原。";
            Status = "本地资源已移到回收站";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or OperationCanceledException or System.ComponentModel.Win32Exception)
        {
            LocalDownloadMessage = $"未删除文件：{e.Message} 请刷新列表或关闭占用文件的程序后重试。";
            Status = LocalDownloadMessage;
        }
        finally { IsLocalDeleting = false; }
    }
    public void OpenLocalDownloadLocation(LocalContentFile file)
    {
        try
        {
            var path = LocalContentLibrary.ValidateFile(GameDirectory, file);
            var start = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
            start.ArgumentList.Add("/select,"); start.ArgumentList.Add(path);
            Process.Start(start);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        { LocalDownloadMessage = $"无法打开文件位置：{e.Message}"; }
    }
    private void NotifyLocalDownloads()
    {
        foreach (var name in new[] { nameof(FilteredLocalDownloads), nameof(HasNoLocalDownloads), nameof(LocalDownloadSummary) }) Notify(name);
    }
    private void NotifyLocalCommands()
    {
        Notify(nameof(CanManageLocalFiles)); Notify(nameof(HasNoLocalDownloads));
        NotifyCommands(RefreshLocalDownloadsCommand, ConfirmLocalDeletionCommand, CancelLocalDeletionCommand);
    }
}
