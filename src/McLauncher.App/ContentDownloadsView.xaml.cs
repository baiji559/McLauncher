using System.Diagnostics;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using McLauncher.Core;

namespace McLauncher.App;

public partial class ContentDownloadsView : UserControl
{
    private MainViewModel? _observedModel;
    private bool _preparingFeatured;
    private int _viewVersion;
    private Button? _activeDownloadButton;
    private string? _activeDownloadFile;
    public ContentDownloadsView()
    {
        InitializeComponent();
        Loaded += (_, _) => ObserveModel(DataContext as MainViewModel);
        Unloaded += (_, _) => { ++_viewVersion; CloseCardMenus(); ObserveModel(null); };
        IsVisibleChanged += (_, e) => { if (!(bool)e.NewValue) { ++_viewVersion; CloseCardMenus(); } };
        DataContextChanged += (_, _) => { if (IsLoaded) ObserveModel(DataContext as MainViewModel); };
    }
    private void ObserveModel(MainViewModel? model)
    {
        if (ReferenceEquals(model, _observedModel)) return;
        if (_observedModel is not null) PropertyChangedEventManager.RemoveHandler(_observedModel, ResourceModelChanged, "");
        _observedModel = model;
        if (model is not null) PropertyChangedEventManager.AddHandler(model, ResourceModelChanged, "");
    }
    private void ResourceModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.GameDirectory) && LocalTab.IsChecked == true)
            Dispatcher.BeginInvoke(new Action(async () =>
            {
                if (DataContext is MainViewModel model && LocalTab.IsChecked == true) await model.RefreshLocalDownloadsAsync();
            }));
        if (_activeDownloadButton is { } button && sender is MainViewModel active
            && active.SelectedContentFile?.FileName == _activeDownloadFile
            && e.PropertyName is nameof(MainViewModel.Status) or nameof(MainViewModel.IsBusy) or nameof(MainViewModel.ContentMessage))
            button.ToolTip = active.IsBusy ? active.Status : active.ContentSelectionHint.StartsWith("已下载", StringComparison.Ordinal)
                ? active.ContentSelectionHint : active.ContentMessage;
        if (e.PropertyName is not (nameof(MainViewModel.ContentQuery) or nameof(MainViewModel.ContentKind)
            or nameof(MainViewModel.ContentGameVersion) or nameof(MainViewModel.ContentLoader) or nameof(MainViewModel.ContentPlatform))) return;
        if (_preparingFeatured) return;
        ResetScroll();
    }

    public void ShowBrowser()
    {
        if (FeaturedPanel is null) return;
        ShowSection(BrowserContent);
        if (DataContext is not MainViewModel model) return;
        var tab = model.ContentKind switch { ContentKind.Modpack => ModpackTab, ContentKind.Shader => ShaderTab, ContentKind.ResourcePack => ResourcePackTab, _ => ModTab };
        tab.IsChecked = true;
        PageMotion.Play(BrowserPanel, (model.ContentKind, model.ContentQuery));
    }
    private void Featured_Checked(object sender, RoutedEventArgs e)
    {
        if (FeaturedPanel is null) return;
        ShowSection(FeaturedPanel);
        PageMotion.Play(BrowserPanel, "featured");
    }
    private void Category_Checked(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel model || sender is not RadioButton { Tag: string kind } || model.IsBusy) return;
        if (Enum.TryParse<ContentKind>(kind, out var value)) model.ContentKind = value;
        ShowSection(BrowserContent);
        PageMotion.Play(BrowserPanel, (model.ContentKind, model.ContentQuery));
    }
    private void Search_Click(object sender, RoutedEventArgs e)
    {
        ShowBrowser();
        if (DataContext is MainViewModel model && model.SearchContentCommand.CanExecute(null)) model.SearchContentCommand.Execute(null);
    }
    private void ProjectsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || !BrowserContent.IsVisible || !ReferenceEquals(e.OriginalSource, ProjectsList)
            || e.AddedItems.Count == 0 || DataContext is not MainViewModel model) return;
        if (e.AddedItems[0] is ContentProject project) model.SelectedContentProject = project;
        if (model.LoadContentFilesCommand.CanExecute(null)) model.LoadContentFilesCommand.Execute(null);
    }
    private void Query_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Enter) return;
        Search_Click(sender, e); e.Handled = true;
    }
    private void DownloadFile_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (DataContext is not MainViewModel model || model.IsBusy
            || sender is not Button { DataContext: ContentFile file } || !model.ContentFiles.Contains(file)) return;
        // Select the clicked row explicitly: a Button handles mouse input before ListBoxItem can select it.
        model.SelectedContentFile = file;
        var command = string.IsNullOrWhiteSpace(file.DownloadUrl)
            ? model.OpenContentFileWebsiteCommand : model.DownloadContentCommand;
        if (command.CanExecute(null)) command.Execute(null);
    }
    private void BrowseAll_Click(object sender, RoutedEventArgs e) => ShowBrowser();
    private void ShowSection(FrameworkElement panel)
    {
        SetLocalMode(ReferenceEquals(panel, LocalPanel));
        foreach (var section in new FrameworkElement[] { FeaturedPanel, BrowserContent, DetailPanel })
            section.Visibility = ReferenceEquals(section, panel) ? Visibility.Visible : Visibility.Collapsed;
        ++_viewVersion;
        CloseCardMenus();
    }
    private void SetLocalMode(bool local)
    {
        if (LocalPanel is null) return;
        BrowserPanel.Visibility = local ? Visibility.Collapsed : Visibility.Visible;
        LocalPanel.Visibility = local ? Visibility.Visible : Visibility.Collapsed;
        ResourceSearch.Visibility = local ? Visibility.Collapsed : Visibility.Visible;
        ResourceHeader.ClearValue(PageMotion.HeaderSourceProperty);
        ResourceHeader.Height = local ? 96 : 156;
        ResourceTabsRow.Height = new GridLength(local ? 48 : 62);
        ResourceEyebrow.Visibility = local ? Visibility.Collapsed : Visibility.Visible;
        ResourceHeading.Margin = new Thickness(0, 0, local ? 0 : 296, 0);
        ResourceTitle.FontSize = local ? 34 : 42;
        ResourceTitle.Margin = local ? new Thickness(0, 4, 0, 8) : new Thickness(0, 12, 0, 12);
        if (!local) PageMotion.SetHeaderSource(ResourceHeader, BrowserPanel);
        ResourceTitle.Text = local ? "本地已下载" : "资源下载";
        ResourceDescription.Text = local ? "管理共享目录与全部实例中的模组、整合包、资源包和光影。" : "整合包、模组、资源包与光影。为你的世界，发现更多可能。";
        ResetScroll();
    }
    private async void Local_Checked(object sender, RoutedEventArgs e)
    {
        if (LocalPanel is null || DataContext is not MainViewModel model) return;
        ShowSection(LocalPanel);
        await model.RefreshLocalDownloadsAsync();
    }
    private void ResetScroll()
    {
        ScrollWheelRouting.CancelWithin(BrowserPanel);
        BrowserPanel.ScrollToTop();
        PageMotion.ResetHeader(ResourceHeader);
    }
    private void BackToFeatured_Click(object sender, RoutedEventArgs e)
    {
        if (FeaturedTab.IsChecked == true) Featured_Checked(sender, e);
        else FeaturedTab.IsChecked = true;
    }
    private void CloseCardMenus()
    {
        foreach (var card in FeaturedCards.Children.OfType<Grid>())
            foreach (var button in card.Children.OfType<Button>())
                if (button.ContextMenu is { } menu) menu.IsOpen = false;
    }
    private void FeaturedCards_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var columns = e.NewSize.Width >= 980 ? 3 : e.NewSize.Width >= 640 ? 2 : 1;
        if (FeaturedCards.Columns != columns) FeaturedCards.Columns = columns;
    }
    private static ContentProject FeaturedProject(string tag)
    {
        var parts = tag.Split('|', 2);
        var (id, description) = parts[1] switch
        {
            "Cobblemon" => ("cobblemon", "在方块世界中探索、捕捉与养成。"),
            "Complementary" => ("complementary-reimagined", "柔和光照与细腻体积云，适合日常游玩。"),
            _ => ("fresh-animations", "让生物动作更自然，同时保留原版风格。")
        };
        var kind = Enum.Parse<ContentKind>(parts[0]);
        var path = kind == ContentKind.Mod ? "mod" : kind == ContentKind.Shader ? "shader" : "resourcepack";
        return new(ContentPlatform.Modrinth, kind, id, parts[1], description, "Modrinth", 0, $"https://modrinth.com/{path}/{id}");
    }
    private async void FeaturedResource_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (DataContext is not MainViewModel model || model.IsBusy || sender is not Button { Tag: string tag }) return;
        ShowSection(DetailPanel);
        _preparingFeatured = true;
        try { await model.PrepareFeaturedResourceAsync(FeaturedProject(tag)); }
        finally { _preparingFeatured = false; }
    }
    private async void FeaturedDownload_Click(object sender, RoutedEventArgs e)
    {
        // This is a sibling of the detail button, never a nested click target.
        e.Handled = true;
        if (DataContext is not MainViewModel model || model.IsBusy || sender is not Button button || button.Tag is not string tag) return;
        var version = _viewVersion;
        button.ToolTip = "正在读取兼容版本…";
        _preparingFeatured = true;
        try { await model.PrepareFeaturedResourceAsync(FeaturedProject(tag)); }
        finally { _preparingFeatured = false; }
        if (!IsLoaded || !IsVisible || version != _viewVersion || !ReferenceEquals(DataContext, model)) return;
        var menu = new ContextMenu { PlacementTarget = button };
        foreach (var file in model.ContentFiles)
        {
            var item = new MenuItem { Header = file.DisplayName, ToolTip = file.Details, IsEnabled = !string.IsNullOrWhiteSpace(file.DownloadUrl) };
            item.Click += (_, args) =>
            {
                args.Handled = true;
                if (model.IsBusy || version != _viewVersion) return;
                model.SelectedContentFile = file;
                _activeDownloadButton = button;
                _activeDownloadFile = file.FileName;
                if (model.DownloadContentCommand.CanExecute(null)) model.DownloadContentCommand.Execute(null);
                button.ToolTip = model.ContentSelectionHint;
            };
            menu.Items.Add(item);
        }
        if (menu.Items.Count == 0) menu.Items.Add(new MenuItem { Header = model.ContentMessage, IsEnabled = false });
        button.ToolTip = menu.Items.Count > 0 && model.ContentFiles.Count > 0 ? "下载并安装 · 选择兼容版本" : model.ContentMessage;
        menu.Closed += (_, _) => button.ContextMenu = null;
        button.ContextMenu = menu;
        menu.IsOpen = true;
    }

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
