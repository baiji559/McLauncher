using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;

namespace McLauncher.App;

public partial class ContentDownloadsView : UserControl
{
    public ContentDownloadsView() => InitializeComponent();

    private void ApiKey_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel model)
            model.CurseForgeApiKey = ((PasswordBox)sender).Password;
    }

    private void ApiConsole_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("https://console.curseforge.com/") { UseShellExecute = true }); }
        catch (Exception exception) { MessageBox.Show(exception.Message, "无法打开浏览器"); }
        e.Handled = true;
    }
}
