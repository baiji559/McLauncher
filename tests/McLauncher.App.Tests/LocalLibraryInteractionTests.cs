using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using McLauncher.App;
using McLauncher.Core;

internal static class LocalLibraryInteractionTests
{
    public static void Run()
    {
        var root = Path.GetFullPath(Path.Combine("artifacts", "tests", "local-ui-" + Guid.NewGuid().ToString("N")));
        var game = Directory.CreateDirectory(Path.Combine(root, ".minecraft")).FullName;
        var first = Directory.CreateDirectory(Path.Combine(game, "versions", "The Pixelmon Modpack")).FullName;
        var second = Directory.CreateDirectory(Path.Combine(game, "versions", "1.18.2")).FullName;
        var natives = Directory.CreateDirectory(Path.Combine(second, "1.18.2-natives")).FullName;
        File.WriteAllText(Path.Combine(second, "1.18.2.json"), "{}");
        File.WriteAllText(Path.Combine(first, "The Pixelmon Modpack.json"), "{}");
        var sharedVersion = Directory.CreateDirectory(Path.Combine(game, "versions", "26.3")).FullName;
        File.WriteAllText(Path.Combine(sharedVersion, "26.3.json"), "{}");
        string Add(string parent, string folder, string name)
        { var directory = Directory.CreateDirectory(Path.Combine(parent, folder)); var path = Path.Combine(directory.FullName, name); File.WriteAllText(path, "fixture"); return path; }
        var target = Add(second, "mods", "sodium-fabric-0.9.3-beta.4+mc26.3.jar");
        Add(first, "mods", "测试模组.jar.disabled");
        Add(first, "shaderpacks", "ComplementaryReimagined_r5.9.3.zip");
        Add(game, "resourcepacks", "FreshAnimations_v1.10.zip");
        Add(second, "modpacks", "整合包.mrpack");
        using var model = new MainViewModel("unused.exe", new SettingsStore(Path.Combine(root, "settings.json")))
        { GameDirectory = natives, ReduceMotion = true, CurrentPage = LauncherPage.Content };
        Require(model.GameDirectory == second && model.SelectedVersion == "1.18.2", "误选 natives 后应自动纠正游戏目录及实例版本。");
        var recycle = typeof(MainViewModel).GetField("_recycleLocalFile", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var recycled = Path.Combine(root, "recycled.jar");
        var calls = 0;
        var window = new MainWindow(model, false) { Width = 1440, Height = 900, Left = -20000, Top = -20000, ShowActivated = false, ShowInTaskbar = false };
        try
        {
            window.Show(); Settle();
            var browser = (ContentDownloadsView)window.FindName("ResourcesBrowser");
            ((RadioButton)browser.FindName("LocalTab")).IsChecked = true;
            Until(() => !model.IsLocalScanBusy && model.LocalDownloads.Count == 5, "本地入口应扫描并显示所有类型和实例的资源。");
            var local = (LocalDownloadsView)browser.FindName("LocalPanel");
            var list = (ListBox)local.FindName("LocalFilesList");
            Require(local.IsVisible && !((FrameworkElement)browser.FindName("BrowserPanel")).IsVisible && list.Items.Count == 5, "本地资源应有独立可见列表。");
            var realFile = model.LocalDownloads.Single(file => file.FullPath == target);
            list.ScrollIntoView(realFile); list.UpdateLayout(); Settle();
            var realRow = (ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(realFile);
            var realDelete = Descendants(realRow).OfType<Button>().Single(button => button.Content as string == "删除");
            using (var locked = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Invoke(realDelete);
                Until(() => model.HasPendingLocalDeletion, "占用文件也应允许显示删除确认。");
                Invoke((Button)local.FindName("ConfirmDelete"));
                Until(() => !model.IsLocalDeleting && model.LocalDownloadMessage.Contains("未删除", StringComparison.Ordinal), "占用文件必须在应用内提示失败。");
                Require(System.IO.File.Exists(target), "被占用的文件必须保留。");
                model.CancelLocalDeletionCommand.Execute(null);
            }
            Invoke(realDelete);
            Until(() => model.HasPendingLocalDeletion, "真实删除按钮必须显示确认。");
            Invoke((Button)local.FindName("ConfirmDelete"));
            Until(() => !model.IsLocalDeleting && !model.IsLocalScanBusy && !System.IO.File.Exists(target),
                "真实按钮与真实回收站删除失败：" + model.LocalDownloadMessage);
            Require(model.LocalDownloads.Count == 4 && model.LocalDownloadMessage.Contains("已移到回收站", StringComparison.Ordinal),
                "真实回收站删除必须移除磁盘文件并刷新列表。");
            Add(second, "mods", Path.GetFileName(target));
            model.RefreshDashboard();
            model.RefreshLocalDownloadsCommand.Execute(null);
            Until(() => !model.IsLocalScanBusy && model.LocalDownloads.Count == 5, "重新放入的测试文件应能刷新显示。");
            recycle.SetValue(model, (Action<string>)(path => { calls++; System.IO.File.Move(path, recycled, false); }));
            Save(window, "local-downloads-1440.png");
            window.Width = 1020; window.Height = 640; Settle();
            Save(window, "local-downloads-1020.png");
            var query = (TextBox)local.FindName("LocalQuery");
            query.Text = "Pixelmon"; Settle();
            Require(list.Items.Count == 2, "本地搜索应按实例名筛选。");
            query.Text = ""; model.LocalDownloadKind = ContentKind.Mod; Settle();
            Require(list.Items.Count == 2, "类型筛选应包含停用模组。");
            model.LocalDownloadKind = null; Settle();
            var file = model.LocalDownloads.Single(file => file.FullPath == target);
            list.ScrollIntoView(file); list.UpdateLayout(); Settle();
            var row = (ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(file);
            var delete = Descendants(row).OfType<Button>().Single(button => button.Content as string == "删除");
            delete.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Settle();
            Require(model.PendingLocalDeletion == file && System.IO.File.Exists(target) && calls == 0, "删除入口应先显示具体文件确认，不能立即移除。");
            Save(window, "local-downloads-confirm-1020.png");
            Require(list.ActualHeight >= 54, $"最小窗口下显示删除确认时仍应保留可用的文件列表，实际 {list.ActualHeight}。");
            var buttonBounds = delete.TransformToAncestor(local).TransformBounds(new Rect(delete.RenderSize));
            Require(buttonBounds.Right <= local.ActualWidth, "最小窗口下删除按钮不能超出资源列表右侧。");
            model.CancelLocalDeletionCommand.Execute(null); Settle();
            Require(!model.HasPendingLocalDeletion && System.IO.File.Exists(target), "取消删除应保留文件。");
            delete.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            model.ConfirmLocalDeletionCommand.Execute(null);
            Until(() => calls == 1 && !model.IsLocalDeleting && !model.IsLocalScanBusy && model.LocalDownloads.Count == 4, "确认后应刷新磁盘与本地列表。");
            Require(!System.IO.File.Exists(target) && System.IO.File.Exists(recycled) && model.InstalledMods.Count == 0
                && model.LocalDownloadMessage.Contains("回收站", StringComparison.Ordinal), "删除后应同步模组页并显示恢复途径。");
            var other = model.LocalDownloads.First();
            recycle.SetValue(model, (Action<string>)(_ => throw new UnauthorizedAccessException("fixture locked")));
            model.RequestLocalDeletion(other); model.ConfirmLocalDeletionCommand.Execute(null); Settle();
            Require(System.IO.File.Exists(other.FullPath) && model.LocalDownloadMessage.Contains("未删除", StringComparison.Ordinal), "删除失败必须保留文件并提示可重试原因。");
            model.CancelLocalDeletionCommand.Execute(null);
            typeof(MainViewModel).GetProperty(nameof(MainViewModel.IsGameRunning))!.SetValue(model, true);
            model.RequestLocalDeletion(other);
            Require(model.HasPendingLocalDeletion && model.CanManageLocalFiles, "游戏状态不能阻止管理未被占用的已下载文件。");
            model.CancelLocalDeletionCommand.Execute(null);
            typeof(MainViewModel).GetProperty(nameof(MainViewModel.IsGameRunning))!.SetValue(model, false);
            typeof(MainViewModel).GetProperty(nameof(MainViewModel.IsBusy))!.SetValue(model, true);
            Require(((RadioButton)browser.FindName("LocalTab")).IsEnabled && model.CanManageLocalFiles,
                "后台游戏或在线操作不能禁用本地管理入口和已下载文件的删除。");
            typeof(MainViewModel).GetProperty(nameof(MainViewModel.IsBusy))!.SetValue(model, false);
            query.Text = "不存在的资源"; Settle();
            Require(model.HasNoLocalDownloads && list.Items.Count == 0, "无匹配资源应显示空状态。");
            Save(window, "local-downloads-empty-1020.png");
            query.Text = "";
            var empty = Directory.CreateDirectory(Path.Combine(root, "other-game")).FullName;
            model.GameDirectory = empty;
            Until(() => !model.IsLocalScanBusy && model.LocalDownloads.Count == 0 && model.LocalDownloadScope == empty, "切换游戏目录后不得保留旧资源。");
            ((RadioButton)browser.FindName("ShaderTab")).IsChecked = true; Settle();
            Require(!local.IsVisible && ((FrameworkElement)browser.FindName("BrowserPanel")).IsVisible, "应能从本地管理返回在线资源。");
        }
        finally { window.Close(); }
        var blockedSettings = Directory.CreateDirectory(Path.Combine(root, "blocked-settings.json")).FullName;
        using var blockedModel = new MainViewModel("unused.exe", new SettingsStore(blockedSettings));
        blockedModel.SaveSettings();
        Require(blockedModel.Status.Contains("无法保存设置", StringComparison.Ordinal), "设置目录不可写时应提示问题并保留当前会话。");
        Console.WriteLine("PASS 真实按钮调用真实回收站删除，资源搜索/类型/确认与取消/占用失败保护/模组同步/目录切换，双尺寸布局");
    }
    private static void Invoke(Button button) => ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)!).Invoke();
    private static void Save(Window window, string name)
    {
        var element = (FrameworkElement)window.Content;
        element.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)element.ActualWidth, (int)element.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = System.IO.File.Create(Path.Combine("artifacts", name)); encoder.Save(stream);
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    { for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var descendant in Descendants(child)) yield return descendant; } }
    private static void Until(Func<bool> condition, string message)
    { var watch = Stopwatch.StartNew(); while (!condition() && watch.ElapsedMilliseconds < 5000) Settle(); Require(condition(), message); }
    private static void Settle()
    { var frame = new DispatcherFrame(); var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(70) }; timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
