using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using McLauncher.App;
using McLauncher.Core;

internal static class WorkflowRegressionTests
{
    public static void Run()
    {
        var root = Path.Combine(Environment.CurrentDirectory, "artifacts", "tests", "workflow-" + Guid.NewGuid().ToString("N"));
        var version = Directory.CreateDirectory(Path.Combine(root, "versions", "fixture-game")).FullName;
        File.WriteAllText(Path.Combine(version, "fixture-game.json"), "{}");
        using var model = new MainViewModel(Environment.ProcessPath!, new SettingsStore(Path.Combine(root, "settings.json"))) { UseOffline = true, GameDirectory = version };
        Require(model.SelectedVersion == "fixture-game", "选择版本文件夹应同步实例。");
        model.GameDirectory = root;
        var handler = new CatalogFixture();
        using var http = new HttpClient(handler);
        typeof(MainViewModel).GetField("_catalog", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(model, new ContentCatalogClient(http));
        var window = new MainWindow(model, false) { Width = 1280, Height = 720, Left = -20000, Top = -20000, Opacity = 0, ShowInTaskbar = false, ShowActivated = false };
        try
        {
            window.Show(); Settle(250);
            model.CurrentPage = LauncherPage.Home;
            model.LaunchCommand.Execute(null);
            Until(() => model.Logs.Any(line => line.Contains("fixture_ready", StringComparison.Ordinal)), "启动后端没有就绪。");
            Require(model.IsBusy && !model.IsGameRunning, "启动准备阶段应显示忙碌状态。");
            window.WindowState = WindowState.Maximized; Settle(200);
            var progress = (ProgressBar)window.FindName("OperationProgress");
            var content = (FrameworkElement)window.Content;
            var bounds = progress.TransformToAncestor(content).TransformBounds(new Rect(progress.RenderSize));
            Require(progress.IsVisible && progress.ActualHeight >= 4 && bounds.Top >= 0 && bounds.Bottom <= content.ActualHeight + .5,
                "最大化时启动进度条必须完整位于窗口内容内。");
            var hwnd = new WindowInteropHelper(window).Handle;
            GetWindowRect(hwnd, out var rect);
            var monitor = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            GetMonitorInfo(MonitorFromWindow(hwnd, 2), ref monitor);
            Require(rect.Left >= monitor.Work.Left - 1 && rect.Top >= monitor.Work.Top - 1 && rect.Right <= monitor.Work.Right + 1 && rect.Bottom <= monitor.Work.Bottom + 1,
                $"最大化窗口必须落在显示器工作区内：窗口 {rect.Left},{rect.Top},{rect.Right},{rect.Bottom}，工作区 {monitor.Work.Left},{monitor.Work.Top},{monitor.Work.Right},{monitor.Work.Bottom}。");
            File.WriteAllText(Path.Combine(root, "allow-launch"), "ready");
            Until(() => model.IsGameRunning && !model.IsBusy, "收到 launched 后必须结束前台忙碌状态。");
            Require(model.LaunchButtonLabel == "游戏运行中" && model.Progress == 100 && !model.IsProgressIndeterminate
                && !model.LaunchCommand.CanExecute(null) && model.SearchContentCommand.CanExecute(null), "游戏运行状态、启动防重复与资源可用性必须独立。");
            model.CurrentPage = LauncherPage.Mods; Settle();
            Descendants((FrameworkElement)window.FindName("ModsHeader")).OfType<Button>().Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Settle();
            var view = (ContentDownloadsView)window.FindName("ResourcesBrowser");
            Require(model.IsContentPage && ((TextBox)view.FindName("Query")).IsEnabled
                && Descendants(view).OfType<ComboBox>().All(combo => combo.IsEnabled), "游戏运行时添加模组跳转后，搜索和筛选不能全部禁用。");
            var a = new ContentProject(ContentPlatform.Modrinth, ContentKind.Mod, "fixture-project-a", "项目 A", "fixture", "fixture", 0, "https://modrinth.com/mod/fixture-project-a");
            var b = a with { Id = "fixture-project-b", Title = "项目 B" };
            model.ContentProjects.Add(a); model.ContentProjects.Add(b);
            var projects = (ListBox)view.FindName("ProjectsList");
            projects.SelectedItem = a;
            Until(() => !model.IsBusy && model.ContentFiles.Count == 1, "选中资源项目没有自动加载版本。");
            Require(model.SelectedContentFile?.FileName == "fixture-project-a.jar" && ((ListBox)view.FindName("FilesList")).Items.Count == 1, "右侧必须显示所选项目的文件。");
            projects.SelectedItem = b;
            Until(() => !model.IsBusy && model.SelectedContentFile?.FileName == "fixture-project-b.jar", "切换项目必须更新右侧版本。");
            Require(handler.Requests == 2, "每次项目选择只应加载一次版本。");
            handler.FailNext = true;
            projects.SelectedItem = a;
            Until(() => !model.IsBusy, "版本读取失败后必须恢复页面操作。");
            Settle();
            Require(model.ContentFiles.Count == 0 && model.ContentMessage.Contains("移除", StringComparison.Ordinal)
                && Descendants(view).OfType<TextBlock>().Any(text => text.IsVisible && text.Text == model.ContentMessage),
                "自动加载失败应清除过期版本并显示原因。");
            model.LoadContentFilesCommand.Execute(null);
            Until(() => !model.IsBusy && model.SelectedContentFile?.FileName == "fixture-project-a.jar", "失败后应能手动重试加载版本。");
            handler.WaitForCancellation = true;
            model.LoadContentFilesCommand.Execute(null);
            Until(() => model.IsBusy, "资源加载应有独立忙碌状态。");
            model.CancelCommand.Execute(null);
            Until(() => !model.IsBusy, "取消资源加载后必须释放前台状态。");
            Require(model.IsGameRunning && model.CanEditContent, "取消资源操作不能终止正在运行的游戏。");
            File.WriteAllText(Path.Combine(root, "exit-game"), "normal");
            Until(() => !model.IsGameRunning && model.LaunchCommand.CanExecute(null), "游戏退出后应恢复启动命令。");
            Require(model.CanEditContent && !model.IsBusy, "游戏退出后资源交互必须保持可用。");

            window.WindowState = WindowState.Normal; Settle();
            model.SelectedVersion = "fixture-failure";
            model.LaunchCommand.Execute(null);
            Until(() => !model.IsBusy && model.LaunchCommand.CanExecute(null), "启动失败后必须释放忙碌状态。");
            Require(!model.IsGameRunning && model.Status.Contains("失败", StringComparison.Ordinal), "启动失败不得残留运行中状态。");
            model.SelectedVersion = "fixture-cancel";
            model.LaunchCommand.Execute(null);
            Until(() => model.Logs.Any(line => line.Contains("fixture_cancel_ready", StringComparison.Ordinal)), "取消夹具未就绪。");
            model.CancelCommand.Execute(null);
            Until(() => !model.IsBusy && model.LaunchCommand.CanExecute(null), "取消启动后必须恢复命令。");
            Require(!model.IsGameRunning && model.CanEditContent, "取消启动不能留下页面禁用状态。");
        }
        finally { window.Close(); }
        Console.WriteLine("PASS 真实后端管道启动/运行/退出/失败/取消，运行时添加模组，项目自动加载版本，最大化工作区与进度条");
    }

    public static async Task RunBackendFixtureAsync(string[] arguments)
    {
        var directory = arguments[Array.IndexOf(arguments, "--main-dir") + 1];
        if (arguments[^1] == "fixture-failure") { Console.WriteLine("error_fixture\tfixture launch failure"); Environment.ExitCode = 7; return; }
        if (arguments[^1] == "fixture-cancel") { Console.WriteLine("fixture_cancel_ready"); await Task.Delay(Timeout.InfiniteTimeSpan); return; }
        Console.WriteLine("fixture_ready");
        Console.WriteLine("download\tfixture\t1/2");
        while (!File.Exists(Path.Combine(directory, "allow-launch"))) await Task.Delay(20);
        Console.WriteLine("launched\t12345");
        while (!File.Exists(Path.Combine(directory, "exit-game"))) await Task.Delay(20);
        Console.WriteLine("terminated\t0");
    }
    private sealed class CatalogFixture : HttpMessageHandler
    {
        public int Requests;
        public bool WaitForCancellation;
        public bool FailNext;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Requests++;
            if (WaitForCancellation) await Task.Delay(Timeout.InfiniteTimeSpan, token);
            if (FailNext) { FailNext = false; return new(HttpStatusCode.NotFound) { Content = new StringContent("fixture not found") }; }
            var id = request.RequestUri!.AbsolutePath.Split('/')[^2];
            return new(HttpStatusCode.OK) { Content = new StringContent("""[{"id":"version-__id__","name":"兼容文件","game_versions":["1.20.1"],"loaders":["fabric"],"version_type":"release","date_published":"2025-01-01T00:00:00Z","files":[{"filename":"__id__.jar","url":"https://example.invalid/__id__.jar","size":1,"hashes":{}}]}]""".Replace("__id__", id), Encoding.UTF8, "application/json") };
        }
    }
    private static void Until(Func<bool> condition,string failure) { var watch=Stopwatch.StartNew(); while(!condition() && watch.ElapsedMilliseconds < 10000) Settle(25); Require(condition(),failure); }
    private static void Settle(int ms=80) { var frame=new DispatcherFrame(); var timer=new DispatcherTimer(DispatcherPriority.Background){Interval=TimeSpan.FromMilliseconds(ms)};timer.Tick+=(_,_)=>{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame); }
    private static void Require(bool condition,string message) { if(!condition) throw new InvalidOperationException(message); }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root) { for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);yield return child;foreach(var descendant in Descendants(child))yield return descendant;} }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public int Flags; }
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll", CharSet=CharSet.Auto)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
}
