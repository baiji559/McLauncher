using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using McLauncher.App;

internal static class PresentationTests
{
    public static void Run()
    {
        var testRoot = Path.GetFullPath(Path.Combine("artifacts", "tests"));
        var directory = Path.Combine(testRoot, "presentation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var vanilla = Directory.CreateDirectory(Path.Combine(directory, "versions", "1.21.1"));
            File.WriteAllText(Path.Combine(vanilla.FullName, "1.21.1.json"), "{}");
            var modded = Directory.CreateDirectory(Path.Combine(directory, "versions", "Survival"));
            File.WriteAllText(Path.Combine(modded.FullName, "Survival.json"), """{"libraries":[{"name":"net.fabricmc:fabric-loader:0.16.0"}]}""");
            var mods = Directory.CreateDirectory(Path.Combine(modded.FullName, "mods"));
            File.WriteAllText(Path.Combine(mods.FullName, "sodium-fabric.jar"), "fixture");
            File.WriteAllText(Path.Combine(mods.FullName, "disabled.jar.disabled"), "fixture");
            File.WriteAllText(Path.Combine(mods.FullName, "readme.txt"), "fixture");

            using var model = new MainViewModel("unused.exe")
            {
                GameDirectory = directory, SelectedVersion = "Survival", UseOffline = true, OfflineUsername = "Alex"
            };
            Require(model.LocalVersions.Count == 2 && model.IsSelectedVersionInstalled, "首页必须读取真实本地版本。");
            Require(model.SelectedLoaderLabel == "Fabric", "重命名实例仍应从 manifest 识别加载器。");
            Require(model.InstalledMods.Count == 1 && model.InstalledMods[0].FileName == "sodium-fabric.jar", "模组列表不得包含禁用文件或普通文件。");

            const string longName = "Let's go cobblemon master - Extended Adventure [5.9Beta]";
            foreach (var name in new[] { "26.3", longName })
            {
                var instance = Directory.CreateDirectory(Path.Combine(directory, "versions", name));
                File.WriteAllText(Path.Combine(instance.FullName, name + ".json"), "{}");
            }
            model.RefreshDashboard();

            var window = new MainWindow(model, false);
            var content = (FrameworkElement)window.Content;
            Layout(content, 1280, 820);
            Save(content, "launcher-home-preview.png", 1280, 820);
            Require(((Button)window.FindName("HomeLaunchButton")).Command == model.LaunchCommand, "首页启动按钮必须连接实际启动命令。");

            foreach (var (navigationName, viewName, page) in new[]
                     {
                         ("VersionsNavigation", "VersionsView", LauncherPage.Versions),
                         ("ContentNavigation", "ResourcesView", LauncherPage.Content),
                         ("SettingsNavigation", "SettingsView", LauncherPage.Settings),
                         ("LogsNavigation", "LogsView", LauncherPage.Logs),
                         ("HomeNavigation", "HomeView", LauncherPage.Home)
                     })
            {
                ((RadioButton)window.FindName(navigationName)).IsChecked = true;
                Layout(content, 1280, 820);
                Require(model.CurrentPage == page && ((FrameworkElement)window.FindName(viewName)).Visibility == Visibility.Visible,
                    $"{navigationName} 必须切换到对应页面。");
                Save(content, $"launcher-{page.ToString().ToLowerInvariant()}-preview.png", 1280, 820);
                Layout(content, 1020, 640);
                Save(content, $"launcher-{page.ToString().ToLowerInvariant()}-small.png", 1020, 640);
                var view = (FrameworkElement)window.FindName(viewName);
                var clipped = Descendants(view).OfType<Button>().Where(button => button.ActualWidth > 0
                    && button.ActualWidth + button.Margin.Left + button.Margin.Right < button.DesiredSize.Width - 1).ToArray();
                Require(clipped.Length == 0, "最小窗口按钮被裁切：" + string.Join(", ", clipped.Select(button => button.Content)));
            }

            var titleLabel = new TextBlock { FontFamily = (FontFamily)window.FindResource("MonoFont"), FontSize = 15 };
            var balance = new BalancedTitleConverter();
            var balanced = (string)balance.Convert([longName, 290d, titleLabel], typeof(string), "", System.Globalization.CultureInfo.CurrentCulture);
            Require(balanced.Contains('\n') && string.Concat(balanced.Where(character => !char.IsWhiteSpace(character)))
                == string.Concat(longName.Where(character => !char.IsWhiteSpace(character))), "平衡换行不能丢失版本名内容。");
            Require((string)balance.Convert(["26.3", 290d, titleLabel], typeof(string), "", System.Globalization.CultureInfo.CurrentCulture)
                == "26.3", "短版本号不得添加换行。");

            model.SelectedVersion = longName;
            model.IsHomePage = true;
            Layout(content, 1280, 820);
            Save(content, "launcher-long-title-preview.png", 1280, 820);
            Layout(content, 1020, 640);
            Save(content, "launcher-long-title-small.png", 1020, 640);
            model.SelectedVersion = "Survival";

            model.IsSettingsPage = true;
            Layout(content, 1280, 820);
            var memory = Descendants((FrameworkElement)window.FindName("SettingsView")).OfType<Slider>().Single();
            memory.Value = 8192;
            Layout(content, 1280, 820);
            Require(model.MemoryMb == 8192 && model.MemoryLabel == "8 GB", "内存滑杆必须更新实际运行配置。");
            model.MemoryMb = 4096;

            model.IsVersionsPage = true;
            Layout(content, 1280, 820);
            var installedList = (ListBox)window.FindName("InstalledVersionList");
            installedList.ScrollIntoView(model.LocalVersions.Single(version => version.Id == "26.3"));
            Layout(content, 1080, 740);
            var previewRow = Descendants(installedList).OfType<Button>().Single(button => button.Tag as string == "26.3");
            previewRow.Background = (Brush)window.FindResource("RowHoverBrush");
            Layout(content, 1080, 740);
            SaveRow(previewRow, "version-row-refined.png");
            previewRow.ClearValue(Control.BackgroundProperty);

            installedList.ScrollIntoView(model.LocalVersions.Single(version => version.Id == "1.21.1"));
            Layout(content, 1280, 820);
            var versionButton = Descendants(installedList).OfType<Button>()
                .Single(button => button.Tag as string == "1.21.1");
            versionButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(model.SelectedVersion == "1.21.1" && model.IsHomePage && model.HasNoInstalledMods,
                "选择本地版本后必须回到首页，并刷新对应实例的模组。");

            model.GameDirectory = Path.Combine(directory, "empty");
            Layout(content, 1280, 820);
            Require(model.HasNoLocalVersions && model.HasNoInstalledMods && !model.IsSelectedVersionInstalled,
                "空目录应显示真实空状态。");
            Save(content, "launcher-empty-preview.png", 1280, 820);
            window.Close();
            Console.WriteLine("PASS 首页真实数据、加载器识别、五页导航、版本选择和大小窗口渲染");
        }
        finally
        {
            if (!Path.GetFullPath(directory).StartsWith(testRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("测试清理超出 artifacts/tests。");
            Directory.Delete(directory, true);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Layout(FrameworkElement element, int width, int height)
    {
        element.Measure(new Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();
        element.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    }

    private static void Save(FrameworkElement element, string name, int width, int height)
    {
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine("artifacts", name));
        encoder.Save(stream);
    }

    private static void SaveRow(FrameworkElement row, string name)
    {
        var width = (int)Math.Ceiling(row.ActualWidth);
        var height = (int)Math.Ceiling(row.ActualHeight);
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            drawing.DrawRectangle((Brush)Application.Current.FindResource("CanvasBrush"), null, new Rect(0, 0, width + 32, height + 24));
            drawing.DrawRectangle(new VisualBrush(row), null, new Rect(16, 12, width, height));
        }
        var bitmap = new RenderTargetBitmap(width + 32, height + 24, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
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
}
