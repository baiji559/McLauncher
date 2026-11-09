using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using McLauncher.App;
using McLauncher.Core;

internal static class PresentationTests
{
    public static void Run()
    {
        var testRoot = Path.GetFullPath(Path.Combine("artifacts", "tests"));
        var directory = Path.Combine(testRoot, "soft-voxel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            AddVersion(directory, "1.21.1", "{}");
            var instance = AddVersion(directory, "Better Minecraft", """{"inheritsFrom":"1.20.1","libraries":[{"name":"net.minecraftforge:forge:1.20.1-47.2.0"}]}""");
            var mods = Directory.CreateDirectory(Path.Combine(instance, "mods"));
            foreach (var name in new[] { "JEI-15.20.0.jar", "Sodium-0.6.13.jar", "AppleSkin-2.5.1.jar", "Terralith-2.5.4.jar", "DistantHorizons-2.3.2.jar.disabled", "SimpleVoiceChat-2.5.26.jar" })
                File.WriteAllText(Path.Combine(mods.FullName, name), "fixture");
            File.WriteAllText(Path.Combine(mods.FullName, "readme.txt"), "ignored");
            using var model = new MainViewModel("unused.exe") { GameDirectory = directory, SelectedVersion = "Better Minecraft", UseOffline = true, OfflineUsername = "Alex", MemoryMb = 8192 };
            Require(model.LocalVersions.Count == 2 && model.IsSelectedVersionInstalled, "必须读取真实本地实例。");
            Require(model.SelectedLoaderLabel == "Forge" && model.GameVersionDisplay == "1.20.1", "自定义实例应读取 manifest 版本和加载器。");
            Require(model.InstalledMods.Count == 6 && model.InstalledMods.Count(mod => !mod.IsEnabled) == 1, "模组管理应读取全部启用和禁用文件。");
            var toggle = model.InstalledMods.First(mod => mod.IsEnabled);
            toggle.IsEnabled = false;
            Require(File.Exists(Path.Combine(mods.FullName, toggle.FileName)) && toggle.FileName.EndsWith(".disabled"), "停用模组必须移动实际文件。");
            toggle.IsEnabled = true;
            Require(!toggle.FileName.EndsWith(".disabled") && File.Exists(Path.Combine(mods.FullName, toggle.FileName)), "启用模组必须恢复 jar 文件。");
            model.EnabledModsOnly = true;
            Require(model.FilteredMods.Count() == 5, "已启用筛选必须排除禁用文件。");
            model.EnabledModsOnly = false;
            model.ModQuery = "not-present";
            Require(model.HasNoFilteredMods, "模组搜索必须展示可恢复的空状态。");
            model.ModQuery = "";

            var window = new MainWindow(model, false);
            var content = (FrameworkElement)window.Content;
            Require(Descendants(content).OfType<RadioButton>().Count(button => button.GroupName == "MainNav") == 4,
                "主导航应只保留主页、模组、资源和设置四个入口。");
            foreach (var (navigation, page, viewName) in new[] {
                ("HomeNavigation", LauncherPage.Home, "HomeView"),
                ("ModsNavigation", LauncherPage.Mods, "ModsView"), ("ContentNavigation", LauncherPage.Content, "ResourcesView"),
                ("SettingsNavigation", LauncherPage.Settings, "SettingsView") })
            {
                ((RadioButton)window.FindName(navigation)).IsChecked = true;
                Layout(content, 1440, 900);
                Require(model.CurrentPage == page && ((FrameworkElement)window.FindName(viewName)).Visibility == Visibility.Visible, "顶部导航应切换到正确页面。");
                Save(content, $"soft-voxel-{page.ToString().ToLowerInvariant()}.png", 1440, 900);
                Layout(content, 1280, 720);
                Save(content, $"soft-voxel-{page.ToString().ToLowerInvariant()}-medium.png", 1280, 720);
                Layout(content, 1020, 640);
                Save(content, $"soft-voxel-{page.ToString().ToLowerInvariant()}-small.png", 1020, 640);
                var navs = new[] { "HomeNavigation", "ModsNavigation", "ContentNavigation", "SettingsNavigation" }
                    .Select(name => (FrameworkElement)window.FindName(name)).ToArray();
                for (var i = 0; i < navs.Length - 1; i++)
                    Require(navs[i].TransformToAncestor(content).TransformBounds(new Rect(navs[i].RenderSize)).Right
                        <= navs[i + 1].TransformToAncestor(content).TransformBounds(new Rect(navs[i + 1].RenderSize)).Left + .5,
                        "最小窗口导航不得相互重叠。");
                if (page == LauncherPage.Home)
                {
                    var launch = (Button)window.FindName("HomeLaunchButton");
                    var bounds = launch.TransformToAncestor(content).TransformBounds(new Rect(launch.RenderSize));
                    Require(bounds.Height >= 52 && bounds.Bottom < 590, "小窗口必须保持启动按钮大小并完整显示在首屏。");
                    var actions = ((StackPanel)window.FindName("HomeActions")).Children.OfType<Button>()
                        .Select(button => button.TransformToAncestor(content).TransformBounds(new Rect(button.RenderSize))).ToArray();
                    Require(actions.All(action => Math.Abs(action.Top + action.Height / 2 - (bounds.Top + bounds.Height / 2)) < .5),
                        "启动、目录和设置操作应严格垂直居中对齐。");
                }
            }
            foreach (var page in new[] { LauncherPage.Instance, LauncherPage.Versions, LauncherPage.Logs })
            {
                model.CurrentPage = page; Layout(content, 1440, 900);
                Save(content, $"soft-voxel-{page.ToString().ToLowerInvariant()}.png", 1440, 900);
                Layout(content, 1280, 720); Save(content, $"soft-voxel-{page.ToString().ToLowerInvariant()}-medium.png", 1280, 720);
                Layout(content, 1020, 640); Save(content, $"soft-voxel-{page.ToString().ToLowerInvariant()}-small.png", 1020, 640);
            }
            Require(((Button)window.FindName("HomeLaunchButton")).Command == model.LaunchCommand, "启动按钮必须连接实际命令。");
            model.CurrentPage = LauncherPage.Content;
            var browser = (ContentDownloadsView)window.FindName("ResourcesBrowser");
            browser.ShowBrowser();
            var project = new ContentProject(ContentPlatform.Modrinth, ContentKind.Mod, "sodium", "Sodium", "优化 Minecraft 渲染性能的模组。", "CaffeineMC", 123456, "https://modrinth.com/mod/sodium");
            model.ContentProjects.Add(project); model.SelectedContentProject = project;
            var file = new ContentFile("fixture", "Sodium 0.6.13", "sodium-0.6.13.jar", "https://cdn.modrinth.com/fixture.jar", 1048576, "1.20.1", "Fabric", "release", DateTimeOffset.Now, "https://modrinth.com/mod/sodium");
            model.ContentFiles.Add(file); model.SelectedContentFile = file;
            Layout(content, 1440, 900); Save(content, "soft-voxel-browser.png", 1440, 900);
            var platform = (ComboBox)browser.FindName("Platform");
            Require(Descendants(platform).OfType<TextBlock>().Any(text => text.Text == "Modrinth"), "下拉框关闭时必须显示平台标签，不能显示记录类型。");
            platform.IsDropDownOpen = true;
            Layout(content, 1440, 900);
            var popup = (Popup)platform.Template.FindName("PART_Popup", platform);
            var popupContent = (FrameworkElement)popup.Child;
            Layout(popupContent, 200, 110);
            Save(popupContent, "soft-voxel-platform-dropdown.png", 200, 110);
            Require(Descendants(popupContent).OfType<TextBlock>().Any(text => text.Text == "CurseForge"), "打开平台下拉框必须显示可读标签。");
            platform.IsDropDownOpen = false;
            Layout(content, 1020, 640); Save(content, "soft-voxel-browser-small.png", 1020, 640);
            File.WriteAllText(Path.Combine(mods.FullName, toggle.FileName + ".disabled"), "collision");
            toggle.IsEnabled = false;
            Require(toggle.IsEnabled, "切换失败不得更改状态或覆盖已有文件。");
            File.Delete(Path.Combine(mods.FullName, toggle.FileName + ".disabled"));
            model.IsSettingsPage = true; Layout(content, 1440, 900);
            var memory = Descendants((FrameworkElement)window.FindName("SettingsView")).OfType<Slider>().Single();
            memory.Value = 6144;
            Require(model.MemoryMb == 6144 && model.MemoryLabel == "6 GB", "内存设置必须更新实际配置。");
            ((RadioButton)window.FindName("GeneralSettingsTab")).IsChecked = true;
            Layout(content, 1440, 900);
            Save(content, "soft-voxel-settings-general.png", 1440, 900);
            Require(((FrameworkElement)window.FindName("GeneralSettingsPanel")).Visibility == Visibility.Visible, "设置分类必须可切换。");
            model.VersionQuery = "forge";
            Require(model.FilteredVersions.Count() == 1, "版本搜索应支持加载器。");
            model.VersionQuery = "no-match";
            ((Button)window.FindName("ClearVersionSearchButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(model.VersionQuery == "", "清除搜索应恢复列表。");
            model.IsVersionsPage = true; Layout(content, 1440, 900);
            var version = Descendants((ListBox)window.FindName("InstalledVersionList")).OfType<Button>().Single(button => button.Tag as string == "1.21.1");
            version.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(model.SelectedVersion == "1.21.1" && model.IsHomePage && model.HasNoInstalledMods,
                "切换实例应刷新当前模组。");
            const string longName = "Let's go cobblemon master - Extended Adventure [5.9Beta]";
            AddVersion(directory, longName, """{"inheritsFrom":"1.20.1"}""");
            model.SelectedVersion = longName; Layout(content, 1020, 640);
            Save(content, "soft-voxel-long-title-small.png", 1020, 640);
            model.GameDirectory = Path.Combine(directory, "empty");
            Require(model.HasNoLocalVersions && model.HasNoInstalledMods, "空目录不得填入示例内容。");
            model.CurrentPage = LauncherPage.Home; Layout(content, 1440, 900); Save(content, "soft-voxel-empty-home.png", 1440, 900);
            model.CurrentPage = LauncherPage.Mods; Layout(content, 1440, 900); Save(content, "soft-voxel-empty-mods.png", 1440, 900);
            window.Close();
            Console.WriteLine("PASS Soft Voxel 页面导航、双尺寸渲染、实际模组切换、搜索与实例切换");
        }
        finally
        {
            if (!Path.GetFullPath(directory).StartsWith(testRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("清理超出测试目录。");
            Directory.Delete(directory, true);
        }
    }
    private static string AddVersion(string root, string name, string manifest)
    {
        var directory = Directory.CreateDirectory(Path.Combine(root, "versions", name));
        File.WriteAllText(Path.Combine(directory.FullName, name + ".json"), manifest);
        return directory.FullName;
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Layout(FrameworkElement element, int width, int height)
    {
        element.Measure(new Size(width, height)); element.Arrange(new Rect(0, 0, width, height)); element.UpdateLayout();
        element.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    }
    private static void Save(FrameworkElement element, string name, int width, int height)
    {
        // Shared control feedback still animates on detached render fixtures. Capture its resting state.
        var frame = new DispatcherFrame();
        var settle = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(160) };
        settle.Tick += (_, _) => { settle.Stop(); frame.Continue = false; };
        settle.Start(); Dispatcher.PushFrame(frame);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(element);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine("artifacts", name)); encoder.Save(stream);
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        { var child = VisualTreeHelper.GetChild(root, i); yield return child; foreach (var descendant in Descendants(child)) yield return descendant; }
    }
}
