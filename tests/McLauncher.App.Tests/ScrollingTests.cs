using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using McLauncher.App;
using McLauncher.Core;

internal static class ScrollingTests
{
    public static void Run()
    {
        using var model = new MainViewModel("unused.exe")
        {
            GameDirectory = Path.Combine(Environment.CurrentDirectory, "artifacts", "scrolling-fixture"),
            UseOffline = true,
            CurrentPage = LauncherPage.Logs
        };
        for (var i = 0; i < 160; i++) model.Logs.Add($"日志 {i:000} " + new string('x', 200));
        var window = new MainWindow(model, false)
        {
            Width = 1020, Height = 640, Left = -20000, Top = -20000,
            ShowActivated = false, ShowInTaskbar = false
        };
        try
        {
            window.Show();
            Settle(350);
            var list = (ListBox)window.FindName("LogList");
            var listScroller = Descendants(list).OfType<ScrollViewer>().First();
            Require(listScroller.CanContentScroll && listScroller.ScrollableHeight > 100, $"列表应保留虚拟化，并具有可滚动内容：CanContentScroll={listScroller.CanContentScroll}，ScrollableHeight={listScroller.ScrollableHeight}，页面={model.CurrentPage}。");
            listScroller.ScrollToVerticalOffset(37.5);
            Settle();
            Require(Math.Abs(listScroller.VerticalOffset - 37.5) < 1, "列表应按像素滚动，而非逐项跳转。");
            var rows = Descendants(list).OfType<ListBoxItem>().ToArray();
            Require(rows.Length > 0 && rows.Length < model.Logs.Count, "长列表必须只创建可见区域附近的行。");
            Require(rows.All(row => row.Opacity >= .85 && row.HorizontalContentAlignment == HorizontalAlignment.Stretch), "列表入场时应可见，并保留原有行布局。");
            listScroller.ScrollToVerticalOffset(1800); Settle(20);
            Require(Descendants(list).OfType<ListBoxItem>().All(row => Math.Abs(row.Opacity - 1) < .01 && row.RenderTransform.Value.IsIdentity),
                "虚拟化回收行时不得重新淡入或位移。");
            listScroller.ScrollToVerticalOffset(37.5); Settle();
            listScroller.ScrollToHorizontalOffset(40);
            Settle();
            Require(listScroller.HorizontalOffset > 0, "日志水平滚动必须可用。");
            var horizontal = Descendants(listScroller).OfType<ScrollBar>().First(bar => bar.Orientation == Orientation.Horizontal);
            var horizontalTrack = (Track)horizontal.Template.FindName("PART_Track", horizontal);
            Require(horizontalTrack.Orientation == Orientation.Horizontal, "水平滚动条的轨道方向应正确。");
            list.SelectedIndex = 2;
            Settle();
            var selected = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(2);
            Require(selected.IsSelected && selected.Template.FindName("Face", selected) is Border { Background: SolidColorBrush brush } && brush.Color.A > 0,
                "新入场样式必须保留选中行的可见底色。");

            model.CurrentPage = LauncherPage.Settings;
            ((RadioButton)window.FindName("GeneralSettingsTab")).IsChecked = true;
            Settle();
            var settings = (FrameworkElement)window.FindName("SettingsView");
            var quietStyle = (Style)window.FindResource("QuietScrollViewer");
            var scroller = Descendants(settings).OfType<ScrollViewer>().Single(view => ReferenceEquals(view.Style, quietStyle));
            Require(scroller.ScrollableHeight > 0, "小窗口设置内容应可滚动。");
            scroller.ScrollToVerticalOffset(40);
            Settle();
            Require(scroller.VerticalOffset > 0, "自定义滚动模板必须能更新实际内容偏移。");
            var vertical = (ScrollBar)scroller.Template.FindName("PART_VerticalScrollBar", scroller);
            var track = (Track)vertical.Template.FindName("PART_Track", vertical);
            Require(track.Orientation == Orientation.Vertical, "垂直滚动条的轨道方向应正确。");
            Require(vertical.ActualWidth <= 8.1 && track.Thumb.MinHeight >= 38, $"滚动条应保持 8 DIP 厚度和 38 DIP 最小滑块：Width={vertical.ActualWidth}, MinThumb={track.Thumb.MinHeight}。");
            var before = scroller.VerticalOffset;
            track.Thumb.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
            track.Thumb.RaiseEvent(new DragDeltaEventArgs(0, 20) { RoutedEvent = Thumb.DragDeltaEvent });
            track.Thumb.RaiseEvent(new DragCompletedEventArgs(0, 20, false) { RoutedEvent = Thumb.DragCompletedEvent });
            Settle();
            Require(scroller.VerticalOffset > before, "拖动自定义滚动条应移动设置内容。");
            VerifyResourceWheel(window, model);
            CompactResourceTests.Run(window, model);
            VerifyPageMotion(window, model);
            SmoothScrollTests.AppearanceAndLifetime(window, model);
            Console.WriteLine("PASS 像素滚动、列表虚拟化、入场可见性、选中态及双方向滚动条");
        }
        finally { window.Close(); }
    }

    private static void VerifyResourceWheel(MainWindow window, MainViewModel model)
    {
        model.CurrentPage = LauncherPage.Content;
        model.ContentKind = ContentKind.ResourcePack;
        var view = (ContentDownloadsView)window.FindName("ResourcesBrowser");
        view.ShowBrowser();
        for (var i = 0; i < 40; i++) model.ContentProjects.Add(new ContentProject(
            ContentPlatform.Modrinth, ContentKind.ResourcePack, $"fixture-{i}", $"资源项目 {i}",
            $"说明 {i}：用于验证鼠标停在左侧文字上时仍然可以上下滚动。", "测试作者", i, "https://modrinth.com"));
        for (var i = 0; i < 8; i++) model.ContentFiles.Add(new ContentFile(
            $"file-{i}", $"文件测试 {i}", $"fixture-{i}.zip", null, 1000,
            "1.20.1", "原版", "release", DateTimeOffset.Now, "https://modrinth.com"));
        Settle(350);
        var outer = (ScrollViewer)view.FindName("BrowserPanel");
        var projects = (ListBox)view.FindName("ProjectsList");
        var inner = Descendants(projects).OfType<ScrollViewer>().First();
        TextBlock ProjectText() => Descendants(projects).OfType<TextBlock>().First(text => text.Text.StartsWith("说明 ", StringComparison.Ordinal));
        Require(outer.ScrollableHeight > 0 && inner.ScrollableHeight < 1, "测试应覆盖外层页面可滚动、内层列表不能滚动的嵌套布局。");
        SmoothScrollTests.Run(window, model, outer, ProjectText);
        outer.ScrollToTop(); Settle();
        Wheel(ProjectText(), -120); Settle(220);
        Require(outer.VerticalOffset > 0, "鼠标停在左侧资源说明上时，向下滚轮应滚动外层页面。");
        var down = outer.VerticalOffset;
        Wheel(ProjectText(), 120); Settle(220);
        Require(outer.VerticalOffset < down, "鼠标停在左侧文字上时，向上滚轮也必须响应。");
        var files = (ListBox)view.FindName("FilesList");
        var fileText = Descendants(files).OfType<TextBlock>().First(text => text.Text.StartsWith("文件测试 ", StringComparison.Ordinal));
        Wheel(fileText, -120); Settle(220);
        Require(outer.VerticalOffset > 0, "右侧文件文字区域也应传递滚轮。");

        projects.MaxHeight = 180;
        projects.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
        outer.ScrollToTop(); Settle(350);
        inner.ScrollToTop(); Settle();
        Require(inner.ScrollableHeight > 0 && outer.ScrollableHeight > 0, "测试还应覆盖内外层都能滚动的布局。");
        Wheel(ProjectText(), -120); Settle(220);
        Require(inner.VerticalOffset > 0 && outer.VerticalOffset < 1, "内层尚能滚动时，应优先滚动列表自身。");
        inner.ScrollToBottom(); outer.ScrollToTop(); Settle();
        Wheel(ProjectText(), -120); Settle(220);
        Require(outer.VerticalOffset > 0, "内层到达底部后，应继续滚动外层页面。");
        inner.ScrollToTop(); outer.ScrollToBottom(); Settle();
        var bottom = outer.VerticalOffset;
        Wheel(ProjectText(), 120); Settle(220);
        Require(outer.VerticalOffset < bottom, "内层到达顶部后，应向上滚动外层页面。");
        projects.ClearValue(FrameworkElement.MaxHeightProperty);
        projects.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
        outer.ScrollToTop(); Settle(350);
        Save((FrameworkElement)window.Content, "resource-wheel-fixed.png");
        Console.WriteLine("PASS 资源文字区域滚轮、内层优先及顶部/底部滚动接续");
    }

    private static void VerifyPageMotion(MainWindow window, MainViewModel model)
    {
        var content = (FrameworkElement)window.FindName("PageContent");
        var transform = ((TransformGroup)content.RenderTransform).Children.OfType<TranslateTransform>().Last();
        model.CurrentPage = LauncherPage.Settings;
        Settle(30);
        Require(content.Opacity >= .9 && content.Opacity <= 1 && content.IsHitTestVisible, "页面切换中内容应保持可见和可操作。");
        Save((FrameworkElement)window.Content, "page-motion-arrival.png");
        foreach (var page in new[] { LauncherPage.Mods, LauncherPage.Settings, LauncherPage.Content, LauncherPage.Home })
        {
            model.CurrentPage = page; Settle(20);
            Require(transform.Y >= 0 && transform.Y <= 12, "快速切页不得累积位移。");
        }
        Settle(350);
        Require(model.IsHomePage && Math.Abs(transform.Y) < .01 && Math.Abs(content.Opacity - 1) < .01,
            "切页结束后必须停在最后选择的页面并恢复正常位置与透明度。");
        PageMotion.Play(content);
        model.ReduceMotion = true;
        Settle();
        Require(Math.Abs(transform.Y) < .01 && Math.Abs(content.Opacity - 1) < .01 && !transform.HasAnimatedProperties,
            "减少动态效果应立即停止空间移动并保留完整内容。");
        model.CurrentPage = LauncherPage.Settings;
        Settle();
        Require(Math.Abs(transform.Y) < .01 && Math.Abs(content.Opacity - 1) < .01, "减少动态效果时切页不得产生位移。");
        model.ReduceMotion = false;
        Save((FrameworkElement)window.Content, "page-motion-settled.png");
        Console.WriteLine("PASS 页面动效可见性、快速切换、稳定终态和减少动态效果");
    }

    private static void Wheel(UIElement source, int delta)
    {
        var args = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta) { RoutedEvent = Mouse.PreviewMouseWheelEvent };
        source.RaiseEvent(args);
        if (args.Handled) return;
        args.RoutedEvent = Mouse.MouseWheelEvent;
        source.RaiseEvent(args);
    }

    private static void Save(FrameworkElement element, string name)
    {
        var bitmap = new RenderTargetBitmap(1020, 640, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine("artifacts", name)); encoder.Save(stream);
    }

    private static void Settle(int milliseconds = 50)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
