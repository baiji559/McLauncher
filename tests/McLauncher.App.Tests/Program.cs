using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using McLauncher.App;
using McLauncher.Core;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        var app = new App();
        app.InitializeComponent();
        var bindingErrors = new BindingErrorListener();
        PresentationTraceSources.DataBindingSource.Listeners.Add(bindingErrors);
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
        using var model = new MainViewModel("unused.exe")
        {
            GameDirectory = Path.Combine(Environment.CurrentDirectory, "artifacts", "ui-game"),
            SelectedVersion = "1.20.1"
        };
        var view = new ContentDownloadsView { DataContext = model };
        Layout(view, 1000, 480);
        var combos = Descendants(view).OfType<ComboBox>().ToArray();
        Require(combos.Length == 3, "平台、类型、加载器下拉框必须存在。");
        Require(combos.All(combo => combo.SelectedIndex == 0), "默认筛选项必须正确绑定。");
        Require(model.ContentUseSystemProxy, "默认应跟随系统代理设置。");
        model.ContentUseSystemProxy = false;
        Require(model.ContentMessage.Contains("直连", StringComparison.Ordinal), "切换直连应显示状态。");
        model.ContentUseSystemProxy = true;
        Require(model.ContentMessage.Contains("系统代理", StringComparison.Ordinal), "切换代理应显示状态。");
        foreach (var (keyword, english) in new[] { ("苹果皮", "AppleSkin"), ("锂", "Lithium"), ("实体渲染机制优化", "Entity Culling") })
        {
            model.ContentQuery = keyword;
            Require(model.ContentSearchHint.Contains(english, StringComparison.Ordinal), "界面应显示中文名对应的查询词。");
        }
        model.ContentKind = ContentKind.ResourcePack;
        Require(!model.ContentSearchHint.Contains("Entity Culling", StringComparison.Ordinal), "资源包搜索不应使用模组别名。");
        model.ContentKind = ContentKind.Mod;
        model.ContentQuery = "";
        model.ContentPlatform = ContentPlatform.CurseForge;
        model.ContentKind = ContentKind.Modpack;
        Layout(view, 1000, 480);
        Require(combos[0].SelectedIndex == 1 && combos[1].SelectedIndex == 1, "双向平台与类型绑定失败。");
        Require(model.ContentDestination.EndsWith("modpacks", StringComparison.Ordinal), "整合包目录错误。");
        model.ContentPlatform = ContentPlatform.Modrinth;
        model.ContentKind = ContentKind.Shader;
        Require(model.ContentLoaders.Any(loader => loader.Value == "iris"), "Modrinth 光影必须支持 Iris 筛选。");
        model.ContentLoader = "iris";
        model.ContentPlatform = ContentPlatform.CurseForge;
        Require(model.ContentLoader == "" && model.ContentLoaders.Count == 1, "切换平台必须清除不支持的加载器。");

        model.ContentPlatform = ContentPlatform.Modrinth;
        model.ContentKind = ContentKind.Mod;
        model.ContentGameVersion = "1.20.1";
        model.ContentLoader = "fabric";
        var project = new ContentProject(ContentPlatform.Modrinth, ContentKind.Mod, "sodium", "Sodium", "优化 Minecraft 渲染性能的模组。", "CaffeineMC", 12345678, "https://modrinth.com/mod/sodium");
        model.ContentProjects.Add(project);
        model.SelectedContentProject = project;
        var file = new ContentFile("v1", "Sodium 0.5.13", "sodium-fabric-0.5.13+mc1.20.1.jar", "https://cdn.modrinth.com/test.jar", 1048576, "1.20.1", "fabric", "release", DateTimeOffset.Parse("2025-01-01T00:00:00Z"), "https://modrinth.com/mod/sodium/version/v1");
        model.ContentFiles.Add(file);
        model.SelectedContentFile = file;
        Require(model.DownloadContentCommand.CanExecute(null), "可下载文件应启用下载按钮。");
        model.SelectedContentFile = file with { DownloadUrl = null };
        Require(!model.DownloadContentCommand.CanExecute(null) && model.OpenContentFileWebsiteCommand.CanExecute(null), "受限文件应提供官方网页入口。");
        model.SelectedContentFile = file;
        Layout(view, 1000, 480);
        SavePreview(view, "downloads-preview.png", 1000, 480);
        Layout(view, 840, 365);
        SavePreview(view, "downloads-small-preview.png", 840, 365);
        var clipped = Descendants(view).OfType<Button>().Where(button => button.ActualWidth + button.Margin.Left + button.Margin.Right < button.DesiredSize.Width - 1).ToArray();
        Require(clipped.Length == 0, "最小窗口下按钮被裁切：" + string.Join(", ", clipped.Select(button => $"{button.Content} ({button.ActualWidth}/{button.DesiredSize.Width})")));
        var networkSettings = Descendants(view).OfType<Expander>().Single();
        networkSettings.IsExpanded = true;
        Layout(view, 840, 365);
        SavePreview(view, "network-settings-small-preview.png", 840, 365);
        var proxyToggle = Descendants(view).OfType<CheckBox>().Single();
        Require(proxyToggle.IsChecked == true, "代理选项应绑定到当前网络模式。");
        proxyToggle.IsChecked = false;
        view.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        Require(!model.ContentUseSystemProxy, "代理选项必须支持双向切换。");
        networkSettings.IsExpanded = false;
        model.ContentQuery = "new search";
        Require(model.ContentProjects.Count == 0 && model.ContentFiles.Count == 0 && !model.DownloadContentCommand.CanExecute(null), "修改筛选条件必须清除过期结果。");
        model.ContentPlatform = ContentPlatform.CurseForge;
        model.CurseForgeApiKey = "";
        model.SearchContentCommand.Execute(null);
        view.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        Require(!model.IsBusy && model.ContentMessage.Contains("API Key", StringComparison.Ordinal), "缺少密钥时应提示原因并恢复操作按钮。");
        Require(bindingErrors.Errors.Count == 0, string.Join(Environment.NewLine, bindingErrors.Errors));
        Console.WriteLine("PASS 下载页 WPF 绑定、筛选切换、受限文件与最小尺寸布局");
    }

    private static void Layout(FrameworkElement view, double width, double height)
    {
        view.Measure(new Size(width, height));
        view.Arrange(new Rect(0, 0, width, height));
        view.UpdateLayout();
        view.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    }
    private static void SavePreview(FrameworkElement view, string name, int width, int height)
    {
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(view);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory("artifacts");
        using var stream = File.Create(Path.Combine("artifacts", name));
        encoder.Save(stream);
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    private sealed class BindingErrorListener : TraceListener
    {
        public List<string> Errors { get; } = [];
        public override void Write(string? message) { if (!string.IsNullOrWhiteSpace(message)) Errors.Add(message); }
        public override void WriteLine(string? message) => Write(message);
    }
}
