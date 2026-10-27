using System.Collections.Specialized;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;

namespace McLauncher.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        var backend = Path.Combine(AppContext.BaseDirectory, "Tools", "portablemc", "portablemc.exe");
        DataContext = _viewModel = new MainViewModel(backend);
        _viewModel.Logs.CollectionChanged += Logs_CollectionChanged;
        Loaded += async (_, _) => await _viewModel.InitializeAsync();
        Closed += (_, _) =>
        {
            _viewModel.SaveSettings();
            _viewModel.Dispose();
        };
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
