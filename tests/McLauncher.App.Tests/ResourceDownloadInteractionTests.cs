using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using McLauncher.App;
using McLauncher.Core;

internal static class ResourceDownloadInteractionTests
{
    public static void Run()
    {
        var root = Path.Combine(Environment.CurrentDirectory, "artifacts", "tests", "resource-click-" + Guid.NewGuid().ToString("N"));
        using var model = new MainViewModel("unused.exe", new SettingsStore(Path.Combine(root, "settings.json")))
        { GameDirectory = root, SelectedVersion = "1.20.1", ReduceMotion = true };
        var handler = new DownloadFixture();
        using var http = new HttpClient(handler);
        typeof(MainViewModel).GetField("_downloader", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(model, new ContentDownloader(http));
        var view = new ContentDownloadsView { DataContext = model };
        view.ShowBrowser();
        model.ContentKind = ContentKind.Shader;
        var project = new ContentProject(ContentPlatform.Modrinth, ContentKind.Shader, "complementary-reimagined", "测试光影", "fixture", "fixture", 0, "https://modrinth.com/shader/complementary-reimagined");
        model.SelectedContentProject = project;
        ContentFile FileEntry(string id) => new(id, "测试版本 " + id, id + ".zip", "https://example.invalid/" + id + ".zip",
            DownloadFixture.Payload.Length, "1.20.1", "iris", "release", DateTimeOffset.Now, project.WebsiteUrl,
            Convert.ToHexString(SHA512.HashData(DownloadFixture.Payload)));
        var first = FileEntry("first");
        var second = FileEntry("second");
        var third = FileEntry("retry");
        var restricted = FileEntry("restricted") with { DownloadUrl = null };
        foreach (var file in new[] { first, second, third, restricted }) model.ContentFiles.Add(file);
        model.SelectedContentFile = first;
        var window = new Window { Content = view, Width = 1200, Height = 800, Left = -20000, Top = -20000, ShowActivated = false, ShowInTaskbar = false };
        try
        {
            window.Show(); Settle();
            var list = (ListBox)view.FindName("FilesList");
            Require(handler.Requests == 0, "加载或选中首个版本不能自动下载。");
            var secondButton = RowButton(list, second);
            foreach (var point in new[] { new Point(25, 20), new Point(25, secondButton.ActualHeight - 18), new Point(secondButton.ActualWidth - 10, 20) })
            {
                var hit = secondButton.InputHitTest(point) as DependencyObject;
                while (hit is not null && hit is not Button) hit = VisualTreeHelper.GetParent(hit);
                Require(ReferenceEquals(hit, secondButton), "版本标题、下载提示和行内空白必须命中下载按钮。");
            }
            ((IInvokeProvider)new ButtonAutomationPeer(secondButton).GetPattern(PatternInterface.Invoke)!).Invoke();
            Until(() => model.IsBusy, "点击版本行必须开始下载。");
            secondButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(model.SelectedContentFile == second && Equals(list.SelectedItem, second), "必须下载点击的版本，不能下载默认选中的首个版本。");
            Until(() => !model.IsBusy, "下载结束必须恢复操作。");
            var destination = model.ContentDestination;
            Require(handler.Requests == 1 && System.IO.File.ReadAllBytes(Path.Combine(destination, second.FileName)).SequenceEqual(DownloadFixture.Payload)
                && !System.IO.File.Exists(Path.Combine(destination, first.FileName)), "单击应只下载一次对应光影文件并通过哈希校验。");
            Require(model.LocalDownloads.Any(local => local.FileName == second.FileName), "下载完成后应自动更新本地资源库。");
            Require(VisibleMessage(secondButton, model.ContentMessage) && model.ContentMessage.Contains("已下载", StringComparison.Ordinal), "当前版本行必须显示下载完成与保存位置。");

            handler.FailNext = true;
            var thirdButton = RowButton(list, third);
            thirdButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Until(() => !model.IsBusy, "下载失败必须恢复操作。");
            Require(handler.Requests == 2 && VisibleMessage(thirdButton, model.ContentMessage)
                && !System.IO.File.Exists(Path.Combine(destination, third.FileName)), "下载错误应在当前行可见，且不能留下完成文件。");
            thirdButton.Focus();
            var source = PresentationSource.FromVisual(thirdButton)!;
            thirdButton.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Space) { RoutedEvent = Keyboard.KeyDownEvent });
            thirdButton.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Space) { RoutedEvent = Keyboard.KeyUpEvent });
            Until(() => !model.IsBusy && System.IO.File.Exists(Path.Combine(destination, third.FileName)), "键盘 Space 必须能重试当前版本下载。");
            Require(handler.Requests == 3, "重试只能触发一次下载。");

            var website = new RecordingCommand();
            typeof(MainViewModel).GetProperty(nameof(MainViewModel.OpenContentFileWebsiteCommand))!.SetValue(model, website);
            var restrictedButton = RowButton(list, restricted);
            Require(Descendants(restrictedButton).OfType<TextBlock>().Any(text => text.Text == "前往官网获取 ↗"), "受限文件必须明确提示从官网获取。");
            restrictedButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(website.Count == 1 && handler.Requests == 3 && model.SelectedContentFile == restricted, "受限版本只应打开对应文件网页。");

            ((RadioButton)view.FindName("FeaturedTab")).IsChecked = true; Settle();
            var detail = Descendants((FrameworkElement)view.FindName("FeaturedCards")).OfType<Button>()
                .Single(button => Equals(button.Tag, "Shader|Complementary") && button.ToolTip as string == "查看详情");
            detail.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Settle();
            Require(((FrameworkElement)view.FindName("DetailPanel")).IsVisible, "应进入精选资源详情。");
            var detailButton = RowButton((ListBox)view.FindName("DetailFilesList"), first);
            detailButton.Focus();
            detailButton.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(detailButton)!, 0, Key.Enter) { RoutedEvent = Keyboard.KeyDownEvent });
            Until(() => !model.IsBusy && System.IO.File.Exists(Path.Combine(destination, first.FileName)), "精选详情版本行也必须支持 Enter 下载。");
            Require(handler.Requests == 4 && model.SelectedContentFile == first, "详情下载应复用同一流程且只请求一次。");
        }
        finally { window.Close(); }
        Console.WriteLine("PASS 版本整行点击/键盘/自动化下载，正确文件与哈希，防重复，行内成功/错误，失败重试，受限官网入口，精选详情下载");
    }

    private static Button RowButton(ListBox list, ContentFile file)
    {
        list.ScrollIntoView(file); list.UpdateLayout(); Settle();
        var row = (ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(file);
        return Descendants(row).OfType<Button>().Single();
    }
    private static bool VisibleMessage(Button row, string message) => Descendants(row).OfType<TextBlock>().Any(text => text.IsVisible && text.Text == message);
    private sealed class DownloadFixture : HttpMessageHandler
    {
        public static readonly byte[] Payload = Encoding.UTF8.GetBytes("shader download fixture");
        public int Requests;
        public bool FailNext;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Requests++;
            await Task.Delay(120, token);
            if (FailNext) { FailNext = false; return new(HttpStatusCode.ServiceUnavailable); }
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(Payload) };
        }
    }
    private sealed class RecordingCommand : ICommand
    {
        public int Count;
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => Count++;
        public event EventHandler? CanExecuteChanged { add { } remove { } }
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    { for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var descendant in Descendants(child)) yield return descendant; } }
    private static void Until(Func<bool> condition, string failure)
    { var watch = Stopwatch.StartNew(); while (!condition() && watch.ElapsedMilliseconds < 5000) Settle(); Require(condition(), failure); }
    private static void Settle()
    { var frame = new DispatcherFrame(); var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(25) }; timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
