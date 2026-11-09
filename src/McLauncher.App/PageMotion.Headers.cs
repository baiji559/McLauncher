using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace McLauncher.App;

public static partial class PageMotion
{
    public static readonly DependencyProperty HeaderSourceProperty = DependencyProperty.RegisterAttached(
        "HeaderSource", typeof(FrameworkElement), typeof(PageMotion), new PropertyMetadata(null, HeaderSourceChanged));
    public static readonly DependencyProperty HeaderRoleProperty = DependencyProperty.RegisterAttached(
        "HeaderRole", typeof(string), typeof(PageMotion), new PropertyMetadata(""));
    private static readonly DependencyProperty HeaderStateProperty = DependencyProperty.RegisterAttached("HeaderState", typeof(HeaderState), typeof(PageMotion));
    private static readonly DependencyProperty HeaderProgressProperty = DependencyProperty.RegisterAttached(
        "HeaderProgress", typeof(double), typeof(PageMotion), new PropertyMetadata(0d, HeaderProgressChanged));
    public static void SetHeaderSource(DependencyObject o, FrameworkElement value) => o.SetValue(HeaderSourceProperty, value);
    public static FrameworkElement? GetHeaderSource(DependencyObject o) => (FrameworkElement?)o.GetValue(HeaderSourceProperty);
    public static void SetHeaderRole(DependencyObject o, string value) => o.SetValue(HeaderRoleProperty, value);
    public static string GetHeaderRole(DependencyObject o) => (string)o.GetValue(HeaderRoleProperty);

    private sealed class HeaderState
    {
        public double FullHeight;
        public bool Compact;
        public int Version;
        public ScrollViewer? Viewer;
        public ScrollChangedEventHandler? Scrolled;
        public MouseWheelEventHandler? Wheel;
        public KeyEventHandler? Key;
        public readonly List<(FrameworkElement Element, string Role, Thickness Margin, double Font)> Parts = [];
        public readonly ScaleTransform TitleScale = new();
        public readonly TranslateTransform TitleOffset = new();
    }
    private static void HeaderSourceChanged(DependencyObject o, DependencyPropertyChangedEventArgs e)
    {
        if (o is not FrameworkElement header) return;
        DetachHeader(header);
        header.Loaded -= HeaderLoaded; header.Unloaded -= HeaderUnloaded; header.IsVisibleChanged -= HeaderVisibility;
        if (e.NewValue is null) return;
        header.Loaded += HeaderLoaded; header.Unloaded += HeaderUnloaded; header.IsVisibleChanged += HeaderVisibility;
        if (header.IsLoaded) AttachHeader(header);
    }
    private static void HeaderLoaded(object sender, RoutedEventArgs e) => AttachHeader((FrameworkElement)sender);
    private static void HeaderUnloaded(object sender, RoutedEventArgs e) => DetachHeader((FrameworkElement)sender);
    private static void HeaderVisibility(object sender, DependencyPropertyChangedEventArgs e)
    {
        var header = (FrameworkElement)sender;
        if ((bool)e.NewValue && header.IsLoaded) AttachHeader(header);
        else DetachHeader(header);
    }
    private static IEnumerable<FrameworkElement> HeaderDescendants(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement fe) yield return fe;
            foreach (var descendant in HeaderDescendants(child)) yield return descendant;
        }
    }
    private static void AttachHeader(FrameworkElement header)
    {
        if (!header.IsVisible || header.GetValue(HeaderStateProperty) is not null) return;
        var source = GetHeaderSource(header);
        source?.ApplyTemplate();
        var viewer = source as ScrollViewer ?? (source is null ? null : HeaderDescendants(source).OfType<ScrollViewer>().FirstOrDefault());
        if (viewer is null) return;
        var state = new HeaderState { FullHeight = header.Height, Viewer = viewer };
        foreach (var part in HeaderDescendants(header).Where(p => GetHeaderRole(p) != ""))
        {
            state.Parts.Add((part, GetHeaderRole(part), part.Margin, (part as TextBlock)?.FontSize ?? 0));
            if (GetHeaderRole(part) == "Title")
            {
                var group = new TransformGroup();
                group.Children.Add(state.TitleScale); group.Children.Add(state.TitleOffset);
                part.RenderTransform = group;
            }
        }
        header.SetValue(HeaderStateProperty, state);
        state.Scrolled = (_, e) =>
        {
            if (!ReferenceEquals(e.OriginalSource, viewer) || Math.Abs(e.ViewportHeightChange) > .01 || Math.Abs(e.VerticalChange) < .01) return;
            if (!state.Compact && viewer.VerticalOffset >= 20) SetHeaderTarget(header, state, true);
            else if (state.Compact && viewer.VerticalOffset <= 6 && e.VerticalChange < 0) SetHeaderTarget(header, state, false);
        };
        state.Wheel = (_, e) => { if (e.Delta > 0 && viewer.VerticalOffset <= 6 && state.Compact) SetHeaderTarget(header, state, false); };
        state.Key = (_, e) => { if (e.Key == Key.Home && state.Compact) SetHeaderTarget(header, state, false); };
        viewer.ScrollChanged += state.Scrolled;
        viewer.PreviewMouseWheel += state.Wheel;
        viewer.PreviewKeyDown += state.Key;
        SetHeaderTarget(header, state, viewer.VerticalOffset >= 20, true);
    }
    public static void ResetHeader(FrameworkElement header)
    {
        if (header.GetValue(HeaderStateProperty) is HeaderState state) SetHeaderTarget(header, state, false);
    }
    private static void SetHeaderTarget(FrameworkElement header, HeaderState state, bool compact, bool immediate = false)
    {
        state.Compact = compact;
        var from = (double)header.GetValue(HeaderProgressProperty);
        var target = compact ? 1d : 0d;
        var version = ++state.Version;
        header.BeginAnimation(HeaderProgressProperty, null);
        header.SetValue(HeaderProgressProperty, target);
        if (!immediate && !GetReduceMotion(header) && SystemParameters.ClientAreaAnimation && Math.Abs(from - target) > .001)
        {
            var animation = new DoubleAnimation(from, target, TimeSpan.FromMilliseconds(210))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop };
            animation.Completed += (_, _) => { if (state.Version == version) header.BeginAnimation(HeaderProgressProperty, null); };
            header.BeginAnimation(HeaderProgressProperty, animation, HandoffBehavior.SnapshotAndReplace);
        }
    }
    private static void HeaderProgressChanged(DependencyObject o, DependencyPropertyChangedEventArgs e)
    {
        if (o is not FrameworkElement header || header.GetValue(HeaderStateProperty) is not HeaderState state) return;
        var p = (double)e.NewValue;
        header.Height = state.FullHeight + (78 - state.FullHeight) * p;
        foreach (var (part, role, margin, font) in state.Parts)
        {
            if (role == "Title" && part is TextBlock title)
            {
                // Scale glyphs without repeatedly measuring text. Height above still releases
                // real layout space to the list, so this does not leave an empty header slot.
                state.TitleScale.ScaleX = state.TitleScale.ScaleY = 1 - .2 * p;
                state.TitleOffset.Y = -20 * p;
            }
            else if (role is "Eyebrow" or "Description") part.Opacity = 1 - p;
            else if (role == "Actions") part.Margin = new Thickness(margin.Left, margin.Top * (1 - p) + 8 * p, margin.Right, margin.Bottom);
        }
    }
    private static void DetachHeader(FrameworkElement header)
    {
        if (header.GetValue(HeaderStateProperty) is not HeaderState state) return;
        if (state.Viewer is { } viewer)
        {
            viewer.ScrollChanged -= state.Scrolled; viewer.PreviewMouseWheel -= state.Wheel; viewer.PreviewKeyDown -= state.Key;
        }
        SetHeaderTarget(header, state, false, true);
        header.ClearValue(HeaderStateProperty);
    }
}
