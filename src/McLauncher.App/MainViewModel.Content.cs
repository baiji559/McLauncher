using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows.Input;
using McLauncher.Core;

namespace McLauncher.App;

public sealed record ContentChoice<T>(T Value, string Label);

public sealed partial class MainViewModel
{
    private HttpClient _contentHttp = null!;
    private bool _contentUseSystemProxy = true;
    private ContentCatalogClient? _catalog;
    private ContentDownloader? _downloader;
    private ContentPlatform _contentPlatform;
    private ContentKind _contentKind;
    private string _contentQuery = "";
    private string _contentGameVersion = "";
    private string _contentLoader = "";
    private string _contentMessage = "输入关键词搜索，留空可浏览热门资源。";
    private ContentProject? _selectedProject;
    private ContentFile? _selectedContentFile;
    private ContentPage<ContentProject>? _projectPage;
    private ContentPage<ContentFile>? _filePage;
    private string? _curseForgeApiKey = Environment.GetEnvironmentVariable("CURSEFORGE_API_KEY");
    private const int ContentPageSize = 20;

    public IReadOnlyList<ContentChoice<ContentPlatform>> ContentPlatforms { get; } =
    [new(ContentPlatform.Modrinth, "Modrinth"), new(ContentPlatform.CurseForge, "CurseForge")];
    public IReadOnlyList<ContentChoice<ContentKind>> ContentKinds { get; } =
    [new(ContentKind.Mod, "模组"), new(ContentKind.Modpack, "整合包"), new(ContentKind.Shader, "光影包"), new(ContentKind.ResourcePack, "资源包")];
    public ObservableCollection<ContentChoice<string>> ContentLoaders { get; } = [];
    public ObservableCollection<ContentProject> ContentProjects { get; } = [];
    public ObservableCollection<ContentFile> ContentFiles { get; } = [];
    public ICommand SearchContentCommand { get; private set; } = null!;
    public ICommand PreviousContentPageCommand { get; private set; } = null!;
    public ICommand NextContentPageCommand { get; private set; } = null!;
    public ICommand LoadContentFilesCommand { get; private set; } = null!;
    public ICommand PreviousFilesPageCommand { get; private set; } = null!;
    public ICommand NextFilesPageCommand { get; private set; } = null!;
    public ICommand DownloadContentCommand { get; private set; } = null!;
    public ICommand OpenContentWebsiteCommand { get; private set; } = null!;
    public ICommand OpenContentFileWebsiteCommand { get; private set; } = null!;
    public ICommand OpenContentFolderCommand { get; private set; } = null!;

    public bool CanEditContent => !IsBusy;
    public bool ContentUseSystemProxy
    {
        get => _contentUseSystemProxy;
        set
        {
            if (IsBusy || !Set(ref _contentUseSystemProxy, value)) return;
            InitializeContentNetwork();
            ContentMessage = value ? "已切换为系统代理；请重新搜索。" : "已切换为直连；请重新搜索。";
            AppendLog(ContentMessage);
        }
    }
    public ContentPlatform ContentPlatform
    {
        get => _contentPlatform;
        set { if (Set(ref _contentPlatform, value)) { UpdateContentLoaders(); ResetContentResults(); } }
    }
    public ContentKind ContentKind
    {
        get => _contentKind;
        set
        {
            if (!Set(ref _contentKind, value)) return;
            UpdateContentLoaders();
            ResetContentResults();
            Notify(nameof(ContentDestination));
            Notify(nameof(ContentDownloadHint));
        }
    }
    public string ContentQuery
    {
        get => _contentQuery;
        set { if (Set(ref _contentQuery, value)) { ResetContentResults(); Notify(nameof(ContentSearchHint)); } }
    }
    public string ContentGameVersion
    {
        get => _contentGameVersion;
        set { if (Set(ref _contentGameVersion, value)) ResetContentResults(); }
    }
    public string ContentLoader
    {
        get => _contentLoader;
        set { if (Set(ref _contentLoader, value ?? "")) ResetContentResults(); }
    }
    // Deliberately session-only: never serialize an API key into settings or logs.
    public string? CurseForgeApiKey { set => _curseForgeApiKey = value; }
    public string ContentMessage { get => _contentMessage; private set => Set(ref _contentMessage, value); }
    public string ContentSearchHint
    {
        get
        {
            if (ContentKind != ContentKind.Mod) return "可输入资源名称搜索；版本和加载器留空时不限范围。";
            var name = ModSearchNames.Find(ContentQuery);
            return name is not null ? $"已识别：{name.Chinese} → {name.English}；保留版本和加载器筛选。"
                : ModSearchNames.ContainsChinese(ContentQuery)
                    ? "此中文名称尚未收录，将按原词搜索；也可尝试英文名。"
                    : "支持常见中文名、英文名和缩写，例如：苹果皮、锂、实体渲染机制优化。";
        }
    }
    public string ContentPageLabel => _projectPage is null ? "尚未搜索" : $"第 {_projectPage.Offset / ContentPageSize + 1} 页 · 共 {_projectPage.Total:N0} 项";
    public string FilesPageLabel => _filePage is null ? "尚未加载版本" : $"第 {_filePage.Offset / ContentPageSize + 1} 页 · 共 {_filePage.Total:N0} 个文件";
    public string ContentDownloadHint => ContentKind switch
    {
        ContentKind.Modpack => "整合包下载为原始 .mrpack / .zip 文件；暂不自动导入或安装加载器。",
        ContentKind.Mod => "请匹配游戏版本与加载器；依赖模组需另行下载。",
        ContentKind.Shader => "下载后请在游戏内启用；需要兼容的 Iris / OptiFine 等光影支持。",
        _ => "下载后请在游戏的资源包设置中启用。"
    };
    public string ContentDestination
    {
        get
        {
            try
            {
                if (string.IsNullOrWhiteSpace(GameDirectory)) return "请先选择游戏目录";
                var location = MinecraftLocation.Resolve(Environment.ExpandEnvironmentVariables(GameDirectory.Trim()));
                return ContentDownloader.GetDestinationDirectory(location.GetGameDirectory(SelectedVersion.Trim()), ContentKind);
            }
            catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
            {
                return "游戏目录无效";
            }
        }
    }
    public ContentProject? SelectedContentProject
    {
        get => _selectedProject;
        set
        {
            if (!Set(ref _selectedProject, value)) return;
            ClearContentFiles();
            NotifyContentCommands();
        }
    }
    public ContentFile? SelectedContentFile
    {
        get => _selectedContentFile;
        set { if (Set(ref _selectedContentFile, value)) NotifyContentCommands(); }
    }

    private void InitializeContentCommands()
    {
        InitializeContentNetwork();
        SearchContentCommand = new AsyncCommand(() => SearchContentAsync(0), () => !IsBusy);
        PreviousContentPageCommand = new AsyncCommand(() => SearchContentAsync(Math.Max(0, (_projectPage?.Offset ?? 0) - ContentPageSize)),
            () => !IsBusy && _projectPage?.Offset > 0);
        NextContentPageCommand = new AsyncCommand(() => SearchContentAsync((_projectPage?.Offset ?? 0) + ContentPageSize),
            () => !IsBusy && _projectPage?.HasNext == true);
        LoadContentFilesCommand = new AsyncCommand(() => LoadContentFilesAsync(0), () => !IsBusy && SelectedContentProject is not null);
        PreviousFilesPageCommand = new AsyncCommand(() => LoadContentFilesAsync(Math.Max(0, (_filePage?.Offset ?? 0) - ContentPageSize)),
            () => !IsBusy && _filePage?.Offset > 0);
        NextFilesPageCommand = new AsyncCommand(() => LoadContentFilesAsync((_filePage?.Offset ?? 0) + ContentPageSize),
            () => !IsBusy && _filePage?.HasNext == true);
        DownloadContentCommand = new AsyncCommand(() => RunAsync("下载资源", DownloadContentAsync),
            () => !IsBusy && !string.IsNullOrWhiteSpace(SelectedContentFile?.DownloadUrl));
        OpenContentWebsiteCommand = new RelayCommand(() => OpenContentUrl(SelectedContentProject?.WebsiteUrl),
            () => SelectedContentProject is not null);
        OpenContentFileWebsiteCommand = new RelayCommand(() => OpenContentUrl(SelectedContentFile?.WebsiteUrl),
            () => SelectedContentFile is not null);
        OpenContentFolderCommand = new RelayCommand(OpenContentFolder, () => !IsBusy);
        UpdateContentLoaders();
    }

    private void InitializeContentNetwork()
    {
        _contentHttp?.Dispose();
        _contentHttp = ContentNetwork.CreateHttpClient(ContentUseSystemProxy,
            message => _dispatcher.BeginInvoke(() => AppendLog(message)));
        _catalog = new ContentCatalogClient(_contentHttp);
        _downloader = new ContentDownloader(_contentHttp);
    }

    private void UpdateContentLoaders()
    {
        ContentLoaders.Clear();
        ContentLoaders.Add(new("", "全部 / 不限"));
        if (ContentKind is ContentKind.Mod or ContentKind.Modpack)
        {
            foreach (var loader in new[] { "forge", "fabric", "quilt", "neoforge" })
                ContentLoaders.Add(new(loader, loader == "neoforge" ? "NeoForge" : char.ToUpperInvariant(loader[0]) + loader[1..]));
        }
        else if (ContentKind == ContentKind.Shader && ContentPlatform == ContentPlatform.Modrinth)
        {
            ContentLoaders.Add(new("iris", "Iris"));
            ContentLoaders.Add(new("optifine", "OptiFine"));
        }
        ContentLoader = "";
    }

    private void ResetContentResults()
    {
        SelectedContentProject = null;
        ContentProjects.Clear();
        ClearContentFiles();
        _projectPage = null;
        ContentMessage = "筛选条件已更新，点击搜索获取资源。";
        Notify(nameof(ContentSearchHint));
        Notify(nameof(ContentPageLabel));
        NotifyContentCommands();
    }

    private void ClearContentFiles()
    {
        SelectedContentFile = null;
        ContentFiles.Clear();
        _filePage = null;
        Notify(nameof(FilesPageLabel));
    }

    private Task SearchContentAsync(int offset) => RunContentOperationAsync("搜索资源", async token =>
    {
        var page = await _catalog!.SearchAsync(new(ContentPlatform, ContentKind, ContentQuery, ContentGameVersion,
            ContentLoader, offset, ContentPageSize), _curseForgeApiKey, token);
        SelectedContentProject = null;
        ClearContentFiles();
        ContentProjects.Clear();
        foreach (var project in page.Items) ContentProjects.Add(project);
        _projectPage = page;
        Notify(nameof(ContentPageLabel));
        ContentMessage = page.Items.Count == 0 ? "当前平台没有找到匹配资源，可清空版本/加载器筛选或切换平台；CurseForge 需要 API Key。" : "选择项目，再点击“查看版本”。";
        Status = $"搜索完成，共 {page.Total:N0} 项";
    });

    private Task LoadContentFilesAsync(int offset) => RunContentOperationAsync("加载资源版本", async token =>
    {
        var project = SelectedContentProject ?? throw new InvalidOperationException("请先选择项目。");
        var page = await _catalog!.GetFilesAsync(project, ContentGameVersion, ContentLoader, offset,
            ContentPageSize, _curseForgeApiKey, token);
        SelectedContentFile = null;
        ContentFiles.Clear();
        foreach (var file in page.Items) ContentFiles.Add(file);
        _filePage = page;
        SelectedContentFile = ContentFiles.FirstOrDefault();
        Notify(nameof(FilesPageLabel));
        ContentMessage = page.Items.Count == 0 ? "没有符合条件的文件，请放宽游戏版本或加载器筛选。"
            : "选择文件后下载；无法直接下载时，可打开文件网页。";
        Status = "资源版本已加载";
    });

    private async Task RunContentOperationAsync(string name, Func<CancellationToken, Task> action)
    {
        if (IsBusy) return;
        await RunAsync(name, async token =>
        {
            try { await action(token); }
            catch (OperationCanceledException exception) when (!token.IsCancellationRequested)
            {
                ContentMessage = "平台请求超时，请检查网络或在“网络设置”切换系统代理/直连后重试。";
                throw new TimeoutException(ContentMessage, exception);
            }
            catch (Exception exception)
            {
                ContentMessage = exception is OperationCanceledException ? "操作已取消。" : exception.Message;
                throw;
            }
        });
    }

    private async Task DownloadContentAsync(CancellationToken token)
    {
        var file = SelectedContentFile ?? throw new InvalidOperationException("请先选择文件。");
        var project = SelectedContentProject ?? throw new InvalidOperationException("请先选择项目。");
        var location = ValidateLocation();
        var destination = ContentDownloader.GetDestinationDirectory(location.GetGameDirectory(ValidateVersion()), project.Kind);
        var progress = new Progress<ContentDownloadProgress>(item =>
        {
            IsProgressIndeterminate = item.Total is not > 0;
            Progress = item.Percentage;
            Status = $"下载 {file.FileName} · {item.Received / 1048576d:0.##} MB";
        });
        try
        {
            var path = await _downloader!.DownloadAsync(file, destination, progress, token);
            Progress = 100;
            ContentMessage = $"已下载：{path}";
            Status = "资源下载完成";
            AppendLog(ContentMessage);
            if (project.Kind == ContentKind.Modpack) AppendLog(ContentDownloadHint);
            Notify(nameof(ContentDestination));
        }
        catch (OperationCanceledException exception) when (!token.IsCancellationRequested)
        {
            ContentMessage = "下载连接超时，请检查网络或切换系统代理/直连后重试。";
            throw new TimeoutException(ContentMessage, exception);
        }
        catch (Exception exception)
        {
            ContentMessage = exception is OperationCanceledException ? "下载已取消，临时文件已清理。" : exception.Message;
            throw;
        }
    }

    private void NotifyContentCommands()
    {
        foreach (var command in new[] { SearchContentCommand, PreviousContentPageCommand, NextContentPageCommand,
            LoadContentFilesCommand, PreviousFilesPageCommand, NextFilesPageCommand, DownloadContentCommand })
            (command as AsyncCommand)?.NotifyCanExecuteChanged();
        foreach (var command in new[] { OpenContentWebsiteCommand, OpenContentFileWebsiteCommand, OpenContentFolderCommand })
            (command as RelayCommand)?.NotifyCanExecuteChanged();
    }

    private void OpenContentUrl(string? address)
    {
        try
        {
            if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
                throw new InvalidOperationException("平台未提供有效网页地址。");
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception exception) { ContentMessage = exception.Message; AppendLog(exception.Message); }
    }

    private void OpenContentFolder()
    {
        try
        {
            var location = ValidateLocation();
            var path = ContentDownloader.GetDestinationDirectory(location.GetGameDirectory(ValidateVersion()), ContentKind);
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception exception) { ContentMessage = exception.Message; AppendLog(exception.Message); }
    }
}
