using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation.Peers;
using System.Windows.Threading;
using Microsoft.Win32;

namespace McLauncher.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(650) };
    private readonly bool _persistSettings;

    public MainWindow() : this(null, true) { }

    public MainWindow(MainViewModel? viewModel, bool initialize)
    {
        _persistSettings = initialize;
        InitializeComponent();
        if (initialize)
        {
            var workArea = SystemParameters.WorkArea;
            MinWidth = Math.Min(MinWidth, workArea.Width);
            MinHeight = Math.Min(640, workArea.Height);
            Width = Math.Clamp(Width, MinWidth, Math.Max(MinWidth, workArea.Width - 32));
            Height = Math.Clamp(Height, MinHeight, Math.Max(MinHeight, workArea.Height - 32));
        }
        DataContext = _viewModel = viewModel ?? new MainViewModel(PortableMcDistribution.GetExecutablePath());
        _viewModel.Logs.CollectionChanged += Logs_CollectionChanged;
        _viewModel.PropertyChanged += Model_PropertyChanged;
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); if (_persistSettings) _viewModel.SaveSettings(); };
        if (initialize) Loaded += async (_, _) => await _viewModel.InitializeAsync();
        Activated += (_, _) => _viewModel.RefreshDashboard();
        StateChanged += (_, _) =>
        {
            WindowFrame.Margin = new Thickness(0);
            MaximizeButton.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
        };
        Closed += (_, _) =>
        {
            ScrollWheelRouting.CancelWithin(WindowFrame);
            _saveTimer.Stop();
            _viewModel.Logs.CollectionChanged -= Logs_CollectionChanged;
            _viewModel.PropertyChanged -= Model_PropertyChanged;
            if (viewModel is null)
            {
                if (initialize) _viewModel.SaveSettings();
                _viewModel.Dispose();
            }
        };
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void LayoutRoot_SizeChanged(object sender, SizeChangedEventArgs e) => AdaptLayout(e.NewSize.Width, e.NewSize.Height);

    private void AdaptLayout(double width, double height)
    {
        if (HomeDetails is null) return;
        var compact = height < 800;
        var narrow = width < 1200;
        HeaderRow.Height = new GridLength(compact ? 110 : 130);
        FooterRow.Height = new GridLength(compact ? 50 : 62);
        NavigationSurface.Margin = new Thickness(42, 28, 42, compact ? 12 : 32);
        BrandColumn.Width = new GridLength(narrow ? 200 : 280);
        AccountColumn.Width = new GridLength(narrow ? 190 : 270);
        AccountStatus.MaxWidth = narrow ? 64 : 110;
        PageContent.Margin = new Thickness(narrow ? 48 : 68, 8, narrow ? 48 : 60, 0);
        var instance = _viewModel?.IsInstancePage == true;
        SceneBackdrop.Margin = new Thickness(Math.Max(410, width * (instance ? .38 : .361)), instance ? 100 : 90, 0, 20);
        HomeDetails.Margin = new Thickness(10, compact ? 2 : 20, 0, 0);
        HomeDetails.Width = Math.Min(535, Math.Max(460, width * .46));
        HomeScroller.Margin = new Thickness(0, 0, 0, compact ? 0 : 42);
        HomeVersionButton.MaxWidth = HomeDetails.Width;
        SelectedVersionTitle.MaxWidth = HomeDetails.Width;
        HomeVersionButton.Margin = new Thickness(-5, compact ? 6 : 17, 0, 0);
        HomeVersionButton.MinHeight = compact ? 80 : 42;
        SelectedVersionTitle.FontSize = compact ? 76 : 108;
        HomeInstanceTitle.FontSize = compact ? 29 : 37;
        HomeInstanceTitle.MaxWidth = HomeDetails.Width;
        HomeSubtitle.FontSize = compact ? 14 : 16;
        HomeSubtitle.Margin = new Thickness(0, compact ? 5 : 8, 0, 0);
        HomeAccent.Margin = new Thickness(0, compact ? 14 : 26, 0, compact ? 9 : 15);
        HomeInstallTitle.FontSize = compact ? 23 : 28;
        HomeDirectorySubtitle.Margin = new Thickness(0, compact ? 7 : 10, 0, 0);
        HomeStatistics.Margin = new Thickness(0, compact ? 14 : 25, 0, 0);
        HomeActions.Margin = new Thickness(0, compact ? 22 : 49, 0, 0);
        HomeLaunchButton.Height = compact ? 56 : 68;
        HomeLaunchButton.FontSize = compact ? 18 : 21;
        HomeRuntime.Margin = new Thickness(0, compact ? 14 : 23, 0, 0);
        LaunchHint.Margin = new Thickness(0, compact ? 5 : 8, 0, 0);
        InstanceKicker.Margin = new Thickness(0, compact ? 14 : 28, 0, 0);
        InstanceTitle.FontSize = compact ? 42 : 58;
        InstanceTitle.Margin = new Thickness(-3, compact ? 16 : 26, 0, 0);
        InstanceVersion.FontSize = compact ? 18 : 22;
        InstanceVersion.Margin = new Thickness(0, compact ? 14 : 20, 0, 0);
        InstanceDescriptionText.Margin = new Thickness(0, compact ? 22 : 40, 0, 0);
        InstanceActions.Margin = new Thickness(0, compact ? 24 : 56, 0, 0);
    }
    private void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void ClearVersionSearch_Click(object sender, RoutedEventArgs e) => _viewModel.VersionQuery = "";

    private void Model_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.CurrentPage))
        {
            AdaptLayout(LayoutRoot.ActualWidth, LayoutRoot.ActualHeight);
            PageMotion.Play(PageContent, _viewModel.CurrentPage);
        }
        if (e.PropertyName is nameof(MainViewModel.ModQuery) or nameof(MainViewModel.EnabledModsOnly))
            ResetListScroll(ModsList);
        if (e.PropertyName == nameof(MainViewModel.VersionQuery)) ResetListScroll(InstalledVersionList);
        if (_persistSettings && e.PropertyName is nameof(MainViewModel.MemoryMb) or nameof(MainViewModel.GameDirectory) or nameof(MainViewModel.SelectedVersion) or nameof(MainViewModel.UseMicrosoft) or nameof(MainViewModel.OfflineUsername) or nameof(MainViewModel.SelectedAccount) or nameof(MainViewModel.ContentUseSystemProxy) or nameof(MainViewModel.ReduceMotion))
        { _saveTimer.Stop(); _saveTimer.Start(); }
        if (e.PropertyName is not (nameof(MainViewModel.Status) or nameof(MainViewModel.LaunchAvailability))) return;
        var target = e.PropertyName == nameof(MainViewModel.Status) ? OperationStatus : LaunchHint;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
            UIElementAutomationPeer.CreatePeerForElement(target)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged));
    }

    private static void ResetListScroll(DependencyObject element)
    {
        if (element is ScrollViewer viewer) { ScrollWheelRouting.Cancel(viewer); viewer.ScrollToTop(); return; }
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(element); i++)
            ResetListScroll(System.Windows.Media.VisualTreeHelper.GetChild(element, i));
    }

    private void Navigate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string page } && Enum.TryParse<LauncherPage>(page, out var destination))
            _viewModel.CurrentPage = destination;
    }

    private void UseVersion_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsBusy || sender is not Button { Tag: string version }) return;
        _viewModel.SelectedVersion = version;
        _viewModel.CurrentPage = LauncherPage.Home;
    }

    private void BrowseResources_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string kind }) return;
        if (!_viewModel.IsBusy && Enum.TryParse<McLauncher.Core.ContentKind>(kind, out var contentKind))
            _viewModel.ContentKind = contentKind;
        _viewModel.CurrentPage = LauncherPage.Content;
        ResourcesBrowser.ShowBrowser();
    }

    private void BrowseDirectory_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择 Minecraft 游戏目录",
            InitialDirectory = Directory.Exists(_viewModel.GameDirectory)
                ? _viewModel.GameDirectory
                : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
        };
        if (dialog.ShowDialog(this) != true) return;
        _viewModel.GameDirectory = dialog.FolderName;
        if (_viewModel.RefreshCommand.CanExecute(null)) _viewModel.RefreshCommand.Execute(null);
    }

    private void Logs_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            if (_viewModel.Logs.Count > 0) LogList.ScrollIntoView(_viewModel.Logs[^1]);
        });
    }

    private void Account_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.CurrentPage = LauncherPage.Settings;
        GeneralSettingsTab.IsChecked = true;
    }

    private void SettingsTab_Checked(object sender, RoutedEventArgs e)
    {
        if (JavaSettingsPanel is null || sender is not RadioButton { Tag: string section }) return;
        GeneralSettingsPanel.Visibility = section == "General" ? Visibility.Visible : Visibility.Collapsed;
        JavaSettingsPanel.Visibility = section == "Java" ? Visibility.Visible : Visibility.Collapsed;
        DownloadSettingsPanel.Visibility = section == "Download" ? Visibility.Visible : Visibility.Collapsed;
        AppearanceSettingsPanel.Visibility = section == "Appearance" ? Visibility.Visible : Visibility.Collapsed;
        AboutSettingsPanel.Visibility = section == "About" ? Visibility.Visible : Visibility.Collapsed;
        ScrollWheelRouting.Cancel(SettingsScroller);
        SettingsScroller.ScrollToTop();
        PageMotion.Play(SettingsContent, section);
    }

    private void RefreshLocal_Click(object sender, RoutedEventArgs e) => _viewModel.RefreshDashboard();
    private void AllMods_Click(object sender, RoutedEventArgs e) => _viewModel.EnabledModsOnly = false;
    private void EnabledMods_Click(object sender, RoutedEventArgs e) => _viewModel.EnabledModsOnly = true;
}
