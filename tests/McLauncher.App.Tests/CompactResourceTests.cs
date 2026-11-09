using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using McLauncher.App;
using McLauncher.Core;

internal static class CompactResourceTests
{
    public static void Run(MainWindow window, MainViewModel model)
    {
        var fixture = Path.Combine(Environment.CurrentDirectory, "artifacts", "mod-ui-fixture.jar");
        File.WriteAllText(fixture, "layout fixture");
        for (var i = 0; i < 120; i++) model.InstalledMods.Add(new InstalledModEntry(fixture, _ => { }));
        model.ModQuery = "mod-ui";
        model.CurrentPage = LauncherPage.Mods;
        Settle(350);
        var list = (ListBox)window.FindName("ModsList");
        var scroller = Descendants(list).OfType<ScrollViewer>().First();
        var header = (FrameworkElement)window.FindName("ModsHeader");
        var fullViewport = scroller.ViewportHeight;
        scroller.ScrollToVerticalOffset(240); Settle(400);
        Require(header.Height <= 79 && scroller.ViewportHeight >= fullViewport + 70, "紧凑标题必须实际释放列表高度。");
        Require(Math.Abs(scroller.VerticalOffset - 240) < 1, "标题变化不能改变列表滚动位置。");
        Require(Descendants(list).OfType<ListBoxItem>().Count() < 40, "紧凑标题必须保留虚拟化回收。");
        scroller.ScrollToVerticalOffset(12); Settle(250);
        Require(header.Height <= 79, "滞后区域不能反复展开。");
        scroller.ScrollToTop(); Settle(350);
        Require(Math.Abs(header.Height - 160) < .1, "回到顶部应恢复完整标题。");
        model.ReduceMotion = true; Settle();
        scroller.ScrollToVerticalOffset(100); Settle();
        Require(header.Height <= 79 && !header.HasAnimatedProperties, "减少动态效果应立即压缩标题。");
        model.CurrentPage = LauncherPage.Settings; Settle();
        Require(Math.Abs(header.Height - 160) < .1 && !header.HasAnimatedProperties, "离开页面必须清理标题时钟。");
        model.CurrentPage = LauncherPage.Mods; Settle();
        Require(header.Height <= 79, "返回页面应按保留的实际偏移恢复标题状态。");
        scroller.ScrollToBottom(); Settle();
        Require(Math.Abs(scroller.VerticalOffset - scroller.ScrollableHeight) < 1, "模组末行必须可滚动到完整位置。");
        var lastSwitch = Descendants(list).OfType<CheckBox>().Last();
        var switchRight = lastSwitch.TransformToAncestor(scroller).TransformBounds(new Rect(lastSwitch.RenderSize)).Right;
        var bar = (ScrollBar)scroller.Template.FindName("PART_VerticalScrollBar", scroller);
        Require(bar.TransformToAncestor(scroller).Transform(new Point()).X - switchRight >= 8, "模组开关必须与滚动条保留安全间距。");

        model.CurrentPage = LauncherPage.Content;
        var view = (ContentDownloadsView)window.FindName("ResourcesBrowser");
        ((RadioButton)view.FindName("FeaturedTab")).IsChecked = true;
        Settle(250);
        var outer = (ScrollViewer)view.FindName("BrowserPanel");
        var cards = (UniformGrid)view.FindName("FeaturedCards");
        foreach (var (width, height, columns) in new[] { (1440,900,3), (1280,720,3), (1020,640,2) })
        {
            window.Width = width; window.Height = height; Settle(300);
            Require(cards.Columns == columns, $"{width}×{height} 资源列数应为 {columns}。");
            outer.ScrollToBottom(); Settle(300);
            foreach (var container in cards.Children.OfType<Grid>())
            {
                var buttons = container.Children.OfType<Button>().ToArray();
                Require(buttons[0].ActualHeight >= 337.5 && buttons[1].ActualWidth >= 44, $"资源描述和下载操作必须完整保留：{buttons[0].ActualHeight}, {buttons[1].ActualWidth}, {buttons[0].IsVisible}, {width}。");
                var bounds = buttons[1].TransformToAncestor(container).TransformBounds(new Rect(buttons[1].RenderSize));
                Require(bounds.Bottom <= container.ActualHeight && bounds.Top >= 0, "下载按钮不能被卡片裁切。");
                Require(buttons[0].Focusable && AutomationProperties.GetName(buttons[0]).StartsWith("查看 "), "整卡必须具有键盘焦点和无障碍名称。");
                foreach (var point in new[] { new Point(30,30), new Point(30,210), new Point(30,270), new Point(30,buttons[0].ActualHeight-30) })
                {
                    var hit = buttons[0].InputHitTest(point) as DependencyObject;
                    Require(hit is not null && Ancestor<Button>(hit) == buttons[0], "封面、标题、描述和空白区域必须命中同一详情按钮。");
                }
            }
            var last = (Grid)cards.Children[cards.Children.Count - 1];
            var bottom = last.TransformToAncestor(outer).TransformBounds(new Rect(last.RenderSize)).Bottom;
            Require(bottom <= outer.ActualHeight - 24, "最后一张卡片必须完整可见并保留底部留白。");
            Save(window, $"resource-complete-{width}.png");
            outer.ScrollToTop(); Settle(300);
        }
        view.Width = 600; Settle(200);
        Require(cards.Columns == 1, "狭窄容器应切换单列，不能继续挤压资源卡片。");
        view.ClearValue(FrameworkElement.WidthProperty); Settle(200);
        var first = (Grid)cards.Children[0];
        var detail = first.Children.OfType<Button>().First();
        var download = first.Children.OfType<Button>().Last();
        var project = new ContentProject(ContentPlatform.Modrinth, ContentKind.Mod, "cobblemon", "Cobblemon", "测试详情", "fixture", 0, "https://modrinth.com/mod/cobblemon");
        model.ContentKind = ContentKind.Mod;
        model.SelectedContentProject = project;
        var file = new ContentFile("fixture", "兼容测试文件", "fixture.jar", "https://example.invalid/fixture.jar", 1, "1.20.1", "fabric", "release", DateTimeOffset.Now, project.WebsiteUrl);
        model.ContentFiles.Add(file); model.SelectedContentFile = file;
        var original = model.DownloadContentCommand;
        var command = new RecordingCommand();
        typeof(MainViewModel).GetProperty(nameof(MainViewModel.DownloadContentCommand))!.SetValue(model, command);
        try
        {
            download.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Settle();
            Require(((FrameworkElement)view.FindName("FeaturedPanel")).IsVisible && !((FrameworkElement)view.FindName("DetailPanel")).IsVisible, "点击下载不得进入详情。");
            Require(download.ContextMenu is { IsOpen: true } && command.Count == 0, "下载入口应先提供兼容文件选择。");
            ((MenuItem)download.ContextMenu!.Items[0]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            download.ContextMenu.IsOpen = false; Settle();
            Require(command.Count == 1 && model.SelectedContentFile == file, "选择兼容文件必须仅执行一次现有下载命令。");
            detail.Focus();
            var source = PresentationSource.FromVisual(detail)!;
            detail.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Space) { RoutedEvent = Keyboard.KeyDownEvent });
            detail.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Space) { RoutedEvent = Keyboard.KeyUpEvent });
            Settle();
            Require(((FrameworkElement)view.FindName("DetailPanel")).IsVisible && command.Count == 1, "Space 必须打开详情且不下载。");
            Descendants((FrameworkElement)view.FindName("DetailPanel")).OfType<Button>().First().RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Settle();
            Require(((FrameworkElement)view.FindName("FeaturedPanel")).IsVisible, "从详情应正常返回精选。");
            detail.Focus();
            detail.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(detail)!, 0, Key.Enter) { RoutedEvent = Keyboard.KeyDownEvent }); Settle();
            Require(((FrameworkElement)view.FindName("DetailPanel")).IsVisible, "Enter 必须进入同一详情。");
            Descendants((FrameworkElement)view.FindName("DetailPanel")).OfType<Button>().First().RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Settle();
            ((IInvokeProvider)new ButtonAutomationPeer(detail).GetPattern(PatternInterface.Invoke)!).Invoke(); Settle();
            Require(((FrameworkElement)view.FindName("DetailPanel")).IsVisible, "自动化 Invoke 必须进入相同详情。");
            ((RadioButton)view.FindName("ShaderTab")).IsChecked = true; Settle();
            Require(outer.VerticalOffset < 1 && ((FrameworkElement)view.FindName("ResourceHeader")).Height == 156, "切换分类应重置滚动和标题状态。");
        }
        finally { typeof(MainViewModel).GetProperty(nameof(MainViewModel.DownloadContentCommand))!.SetValue(model, original); }
        model.ReduceMotion = false;
        var weak = VerifyShortContentAndClose();
        Settle(300);
        for (var i = 0; i < 3 && weak.IsAlive; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); Settle(); }
        Require(!weak.IsAlive, "卸载后标题行为不得持有页面或滚动容器。");
        Console.WriteLine("PASS 紧凑标题/高度释放/虚拟化/减少动态效果，三尺寸资源布局，整卡命中/Space/自动化详情与独立下载");
    }
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference VerifyShortContentAndClose()
    {
        var viewer = new ScrollViewer { Content = new Border { Height = 268 }, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var header = new Grid { Height = 156 };
        PageMotion.SetHeaderSource(header, viewer);
        var root = new Grid(); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition());
        root.Children.Add(header); root.Children.Add(viewer); Grid.SetRow(viewer, 1);
        var window = new Window { Content = root, Width = 400, Height = 400, WindowStyle = WindowStyle.None, Left = -20000, Top = -20000, ShowInTaskbar = false, ShowActivated = false };
        window.Show(); Settle(250);
        viewer.ScrollToBottom(); Settle(350);
        Require(header.Height == 78 && viewer.VerticalOffset < 1, "短内容在视口扩大后应保持紧凑状态，避免边界抖动。");
        viewer.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, 120) { RoutedEvent = Mouse.PreviewMouseWheelEvent }); Settle(350);
        Require(header.Height == 156, "短内容到顶部后继续向上滚动应展开标题。");
        viewer.ScrollToBottom(); Settle(30); window.Close();
        Require(!header.HasAnimatedProperties, "关闭窗口必须立即停止标题动画。");
        window.Content = null;
        return new WeakReference(viewer);
    }
    private sealed class RecordingCommand : ICommand
    {
        public int Count;
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => Count++;
        public event EventHandler? CanExecuteChanged { add { } remove { } }
    }
    private static T? Ancestor<T>(DependencyObject? node) where T : DependencyObject
    { while (node is not null) { if (node is T match) return match; node = VisualTreeHelper.GetParent(node); } return null; }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject node)
    { for (var i=0;i<VisualTreeHelper.GetChildrenCount(node);i++) { var child=VisualTreeHelper.GetChild(node,i); yield return child; foreach(var item in Descendants(child)) yield return item; } }
    private static void Settle(int ms=80)
    { var frame=new DispatcherFrame(); var timer=new DispatcherTimer(DispatcherPriority.Background){Interval=TimeSpan.FromMilliseconds(ms)}; timer.Tick+=(_,_)=>{timer.Stop();frame.Continue=false;}; timer.Start();Dispatcher.PushFrame(frame); }
    private static void Require(bool condition,string message) { if(!condition) throw new InvalidOperationException(message); }
    private static void Save(Window window, string name)
    {
        var bitmap=new RenderTargetBitmap((int)window.Width,(int)window.Height,96,96,PixelFormats.Pbgra32); bitmap.Render((Visual)window.Content);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var stream=File.Create(Path.Combine("artifacts",name));encoder.Save(stream);
    }
}
