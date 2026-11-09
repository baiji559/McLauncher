using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using McLauncher.App;
using McLauncher.Core;

internal static class SmoothScrollTests
{
    public static void Run(MainWindow window, MainViewModel model, ScrollViewer viewer, Func<TextBlock> source)
    {
        model.ReduceMotion = false;
        Settle(180);
        // Warm the first WPF header/template render before measuring a 35 ms sample.
        viewer.ScrollToVerticalOffset(48); Settle(350);
        viewer.ScrollToTop(); Settle(350);
        var step = SystemParameters.WheelScrollLines < 0 ? viewer.ViewportHeight : SystemParameters.WheelScrollLines * 16d;
        if (step <= 0) return;
        void Reset(double offset = 0) { ScrollWheelRouting.Cancel(viewer); viewer.ScrollToVerticalOffset(offset); Settle(); }
        Reset();
        Wheel(source(), -120); Wheel(source(), -120); Wheel(source(), -120);
        if (SystemParameters.ClientAreaAnimation)
            Require(viewer.HasAnimatedProperties && viewer.VerticalOffset < step * 3 - .1, $"滚轮必须启动短动画，不能在事件中立即跳到累计目标：{viewer.VerticalOffset}/{step * 3}。");
        // Software rendering may take longer than the animation duration to service
        // a dispatcher timer; inspect the initial clock before yielding instead.
        Settle(35);
        Settle(240);
        Near(viewer.VerticalOffset, step * 3, "连续输入不能丢失距离");
        Reset();
        Wheel(source(), -120); Settle(45);
        var advancing = viewer.VerticalOffset;
        Wheel(source(), -120); Wheel(source(), -120);
        Settle(20);
        Require(viewer.VerticalOffset >= advancing - .1, "合并连续输入不得回跳。");
        Settle(240); Near(viewer.VerticalOffset, step * 3, "重定向后仍应保留全部累计距离");

        Reset();
        Wheel(source(), -120); Wheel(source(), 120);
        Settle(250); Near(viewer.VerticalOffset, 0, "首帧前反向输入应取消旧方向");
        Reset();
        Wheel(source(), -120); Wheel(source(), -120); Wheel(source(), -120);
        Settle(45);
        var beforeReverse = viewer.VerticalOffset;
        Wheel(source(), 120); Settle(40);
        Require(viewer.VerticalOffset <= beforeReverse + .1, "反向滚动应立即向新方向响应。");
        Settle(240); Near(viewer.VerticalOffset, Math.Max(0, beforeReverse - step), "旧动画不得覆盖反向目标");
        Reset(viewer.ScrollableHeight - 10);
        for (var i = 0; i < 8; i++) Wheel(source(), -120);
        Settle(250); Near(viewer.VerticalOffset, viewer.ScrollableHeight, "底部应准确停止");
        Reset(10);
        for (var i = 0; i < 8; i++) Wheel(source(), 120);
        Settle(250); Near(viewer.VerticalOffset, 0, "顶部应准确停止");

        Reset();
        for (var i = 0; i < 4; i++) Wheel(source(), -15);
        Settle(50); Near(viewer.VerticalOffset, step / 2, "高精度输入应直接跟手并保留每一段距离");
        var direct = viewer.VerticalOffset;
        Settle(220); Near(viewer.VerticalOffset, direct, "高精度输入不应延后滑行");

        Reset(); model.ReduceMotion = true; Settle();
        Wheel(source(), -120); Wheel(source(), -120); Wheel(source(), -120);
        Settle(50); Near(viewer.VerticalOffset, step * 3, "减少动态效果不得改变滚动距离");
        Settle(220); Near(viewer.VerticalOffset, step * 3, "减少动态效果应清除旧动画");
        model.ReduceMotion = false; Settle(150); Reset();
        Wheel(source(), -120); Settle(30);
        model.ReduceMotion = true; Settle(70);
        Near(viewer.VerticalOffset, step, "滚动过程中减少动态效果应完成已输入的距离");
        Settle(220); Near(viewer.VerticalOffset, step, "停止后的旧回调不能覆盖位置");

        model.ReduceMotion = false; Reset();
        Wheel(source(), -120); Settle(25);
        model.CurrentPage = LauncherPage.Settings; Settle(60);
        var hiddenOffset = viewer.VerticalOffset;
        Settle(240); Near(viewer.VerticalOffset, hiddenOffset, "页面切换后隐藏容器不得继续滚动");
        model.CurrentPage = LauncherPage.Content; Settle(300);
        Reset();
        Console.WriteLine("PASS 160ms 平滑滚动、输入合并、立即反向、高精度直达、边界及页面取消");
    }

    public static void AppearanceAndLifetime(MainWindow window, MainViewModel model)
    {
        model.CurrentPage = LauncherPage.Settings;
        var panel = (FrameworkElement)window.FindName("SettingsView");
        Descendants(panel).OfType<RadioButton>().Single(button => button.Content as string == "外观").IsChecked = true;
        Settle(300);
        var toggle = (CheckBox)window.FindName("ReduceMotionToggle");
        toggle.BringIntoView(); Settle();
        var settingsScroller = (ScrollViewer)window.FindName("SettingsScroller");
        var toggleBounds = toggle.TransformToAncestor(settingsScroller).TransformBounds(new Rect(toggle.RenderSize));
        Require(toggleBounds.Top >= -.5 && toggleBounds.Bottom <= settingsScroller.ActualHeight + .5, "小窗口下外观开关必须能滚动到完整可见。");
        toggle.IsChecked = true; Settle();
        Require(model.ReduceMotion && PageMotion.GetReduceMotion(window), "外观设置必须连接 MVVM 和实际动效偏好。");
        Settle(180);
        var thumb = (TranslateTransform)toggle.Template.FindName("SwitchOffset", toggle);
        Near(thumb.X, 22, "开关启用后滑块应到达右侧");
        toggle.IsChecked = false; Settle();
        Require(!model.ReduceMotion && PageMotion.GetReduceMotion(window) == !SystemParameters.ClientAreaAnimation, "关闭显式选项后仍应尊重系统关闭动画。");
        Settle(180); Near(thumb.X, 0, "快速切换开关后滑块应回到左侧");
        var bitmap = new RenderTargetBitmap(1020, 640, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render((FrameworkElement)window.Content);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(Path.Combine("artifacts", "appearance-reduce-motion.png"))) encoder.Save(stream);

        var root = Path.Combine(Environment.CurrentDirectory, "artifacts", "tests", "appearance-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new SettingsStore(Path.Combine(root, "settings.json"));
            using (var initial = new MainViewModel("unused.exe", store) { GameDirectory = root })
            {
                Require(initial.ReduceMotion == !SystemParameters.ClientAreaAnimation, "旧设置文件应默认跟随系统。");
                initial.ReduceMotion = true; initial.SaveSettings();
            }
            using (var reloaded = new MainViewModel("unused.exe", store))
            {
                Require(reloaded.ReduceMotion && store.Load().ReduceMotion == true, "减少动态效果必须持久化。");
                reloaded.ReduceMotion = false; reloaded.SaveSettings();
            }
            Require(store.Load().ReduceMotion == false, "显式关闭也必须正确保存。");
        }
        finally { File.Delete(Path.Combine(root, "settings.json")); Directory.Delete(root); }

        var weak = ClosedViewer();
        Settle(300);
        for (var i = 0; i < 3 && weak.IsAlive; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); Settle(); }
        Require(!weak.IsAlive, "关闭窗口后，滚动动画和事件不得持有容器。");
        Console.WriteLine("PASS 减少动态效果 UI/保存/系统默认及关闭后对象回收");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference ClosedViewer()
    {
        var text = new TextBlock { Text = new string('x', 100), Height = 4000 };
        var viewer = new ScrollViewer { Content = text, CanContentScroll = false };
        var scope = new Border { Child = viewer };
        ScrollWheelRouting.SetIsEnabled(scope, true);
        PageMotion.SetIsEnabled(scope, true);
        var window = new Window { Content = scope, Width = 400, Height = 400, Left = -20000, Top = -20000, ShowActivated = false, ShowInTaskbar = false };
        window.Show(); Settle(300);
        PageMotion.Play(scope);
        Wheel(text, -120);
        window.Close();
        Require(!viewer.HasAnimatedProperties && !scope.HasAnimatedProperties, "关闭窗口应立即移除滚动和页面动画时钟。");
        window.Content = null;
        return new WeakReference(viewer);
    }

    private static void Wheel(UIElement source, int delta)
    {
        var args = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, delta) { RoutedEvent = Mouse.PreviewMouseWheelEvent };
        source.RaiseEvent(args);
        if (args.Handled) return;
        args.RoutedEvent = Mouse.MouseWheelEvent; source.RaiseEvent(args);
    }
    private static void Near(double actual, double expected, string message) => Require(Math.Abs(actual - expected) < 1, $"{message}：{actual:0.##} / {expected:0.##}");
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Settle(int milliseconds = 60)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame);
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i); yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
