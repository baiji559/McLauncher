using System.Windows;
using System.Windows.Controls;
using McLauncher.Core;

namespace McLauncher.App;

public partial class LocalDownloadsView : UserControl
{
    public LocalDownloadsView() => InitializeComponent();
    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel model && sender is Button { DataContext: LocalContentFile file })
        {
            model.RequestLocalDeletion(file);
            if (model.HasPendingLocalDeletion) ConfirmDelete.Focus();
        }
    }
    private void OpenLocation_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel model && sender is Button { DataContext: LocalContentFile file })
            model.OpenLocalDownloadLocation(file);
    }
}
