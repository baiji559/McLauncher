using System.Collections.Specialized;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;

namespace McLauncher.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow() : this(null, true) { }

    public MainWindow(MainViewModel? viewModel, bool initialize)
    {
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
        if (initialize) Loaded += async (_, _) => await _viewModel.InitializeAsync();
        Activated += (_, _) => _viewModel.RefreshDashboard();
        StateChanged += (_, _) =>
        {
            WindowFrame.Margin = WindowState == WindowState.Maximized ? new Thickness(8) : new Thickness(0);
            MaximizeButton.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
        };
        Closed += (_, _) =>
        {
            _viewModel.Logs.CollectionChanged -= Logs_CollectionChanged;
            if (viewModel is null)
            {
                if (initialize) _viewModel.SaveSettings();
                _viewModel.Dispose();
            }
        };
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

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
}
