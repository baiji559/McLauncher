using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace McLauncher.App;

public static class ScrollWheelRouting
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(ScrollWheelRouting), new PropertyMetadata(false, EnabledChanged));
    public static readonly DependencyProperty PreserveNativeProperty = DependencyProperty.RegisterAttached(
        "PreserveNative", typeof(bool), typeof(ScrollWheelRouting), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));
    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached("State", typeof(ScrollState), typeof(ScrollWheelRouting));
    private static readonly DependencyProperty AnimatedOffsetProperty = DependencyProperty.RegisterAttached(
        "AnimatedOffset", typeof(double), typeof(ScrollWheelRouting), new PropertyMetadata(0d, OffsetChanged));
    private static readonly DependencyProperty PrecisionUntilProperty = DependencyProperty.RegisterAttached("PrecisionUntil", typeof(long), typeof(ScrollWheelRouting), new PropertyMetadata(0L));

    private sealed class ScrollState
    {
        public double Target;
        public int Direction;
        public int Version;
        public bool Running;
        public bool Pending;
        public bool Suppress;
        public bool Subscribed;
    }

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);
    public static bool GetPreserveNative(DependencyObject element) => (bool)element.GetValue(PreserveNativeProperty);
    public static void SetPreserveNative(DependencyObject element, bool value) => element.SetValue(PreserveNativeProperty, value);

    private static void EnabledChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not FrameworkElement element) return;
        if ((bool)e.NewValue)
        {
            element.PreviewMouseWheel += RouteWheel;
            element.PreviewMouseDown += InterruptMouse;
            element.PreviewKeyDown += InterruptKey;
            element.PreviewTouchDown += InterruptTouch;
            element.Unloaded += ScopeUnloaded;
        }
        else
        {
            element.PreviewMouseWheel -= RouteWheel;
            element.PreviewMouseDown -= InterruptMouse;
            element.PreviewKeyDown -= InterruptKey;
            element.PreviewTouchDown -= InterruptTouch;
            element.Unloaded -= ScopeUnloaded;
            CancelWithin(element);
        }
    }

    private static void InterruptMouse(object sender, MouseButtonEventArgs e) => CancelWithin((DependencyObject)sender);
    private static void InterruptKey(object sender, KeyEventArgs e) => CancelWithin((DependencyObject)sender);
    private static void InterruptTouch(object? sender, TouchEventArgs e) { if (sender is DependencyObject root) CancelWithin(root); }
    private static void ScopeUnloaded(object sender, RoutedEventArgs e) => CancelWithin((DependencyObject)sender);

    private static void RouteWheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled || e.Delta == 0 || SystemParameters.WheelScrollLines == 0
            || (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) != 0) return;
        var source = e.OriginalSource as DependencyObject;
        if (source is null || GetPreserveNative(source)) return;
        for (var current = source; current is not null && current != sender; current = Parent(current))
            if (current is TextBoxBase or PasswordBox or ComboBox or Slider) return;

        var scope = (DependencyObject)sender;
        // WPF exposes wheel deltas, not a reliable mouse/touchpad device identity.
        // Fractional deltas and the remainder of that short precision burst stay direct.
        var now = Environment.TickCount64;
        if (Math.Abs(e.Delta) % 120 != 0) scope.SetValue(PrecisionUntilProperty, now + 120);
        var precise = now < (long)scope.GetValue(PrecisionUntilProperty);
        for (var current = source; current is not null; current = Parent(current))
        {
            if (current is ScrollViewer pending && !CanScroll(pending, e.Delta)
                && pending.GetValue(StateProperty) is ScrollState { Pending: true } pendingState
                && pendingState.Direction != Math.Sign(e.Delta)) Cancel(pending);
            if (current is ScrollViewer viewer && CanScroll(viewer, e.Delta))
            {
                ApplyWheel(viewer, e.Delta, precise);
                e.Handled = true;
                return;
            }
            if (current == sender) break;
        }
    }

    private static void ApplyWheel(ScrollViewer viewer, int delta, bool precise)
    {
        var state = (ScrollState?)viewer.GetValue(StateProperty);
        if (state is null) { state = new ScrollState(); viewer.SetValue(StateProperty, state); }
        var direction = Math.Sign(delta);
        var reduced = PageMotion.GetReduceMotion(viewer) || !SystemParameters.ClientAreaAnimation;
        var accumulate = state.Pending && state.Direction == direction && !(precise && state.Running);
        var basis = accumulate ? state.Target : viewer.VerticalOffset;
        var distance = SystemParameters.WheelScrollLines < 0 ? viewer.ViewportHeight : SystemParameters.WheelScrollLines * 16d;
        var target = Math.Clamp(basis - delta / 120d * distance, 0, viewer.ScrollableHeight);
        if (state.Running && !precise && !reduced && Math.Abs(target - state.Target) < .01) return;
        var from = viewer.VerticalOffset;
        Cancel(viewer);
        state.Target = target;
        state.Direction = direction;
        state.Pending = true;
        Subscribe(viewer, state);
        if (precise || reduced)
        {
            viewer.ScrollToVerticalOffset(target);
            return;
        }
        state.Running = true;
        var version = ++state.Version;
        state.Suppress = true;
        viewer.SetCurrentValue(AnimatedOffsetProperty, from);
        state.Suppress = false;
        var animation = new DoubleAnimation(from, target, TimeSpan.FromMilliseconds(160))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop
        };
        animation.Completed += (_, _) =>
        {
            if (state.Version != version || !state.Running) return;
            Cancel(viewer);
            viewer.ScrollToVerticalOffset(Math.Clamp(target, 0, viewer.ScrollableHeight));
        };
        viewer.BeginAnimation(AnimatedOffsetProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private static void OffsetChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is ScrollViewer viewer && viewer.GetValue(StateProperty) is ScrollState { Running: true, Suppress: false })
            viewer.ScrollToVerticalOffset(Math.Clamp((double)e.NewValue, 0, viewer.ScrollableHeight));
    }

    private static void Subscribe(ScrollViewer viewer, ScrollState state)
    {
        if (state.Subscribed) return;
        state.Subscribed = true;
        viewer.ScrollChanged += Scrolled;
        viewer.Unloaded += ViewerUnloaded;
        viewer.IsVisibleChanged += VisibilityChanged;
    }
    private static void Scrolled(object sender, ScrollChangedEventArgs e)
    {
        var viewer = (ScrollViewer)sender;
        if (!ReferenceEquals(e.OriginalSource, viewer) || viewer.GetValue(StateProperty) is not ScrollState state) return;
        // Header compaction changes only the viewport. Keep the current wheel clock;
        // OffsetChanged clamps every sample to the new bounds.
        if (Math.Abs(e.ExtentHeightChange) > .01) Cancel(viewer);
        else if (!state.Running && Math.Abs(viewer.VerticalOffset - state.Target) < .1) Cancel(viewer);
    }
    private static void ViewerUnloaded(object sender, RoutedEventArgs e) => Cancel((ScrollViewer)sender);
    private static void VisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!(bool)e.NewValue) Cancel((ScrollViewer)sender);
    }

    public static void Cancel(ScrollViewer viewer, bool completePending = false)
    {
        if (viewer.GetValue(StateProperty) is not ScrollState state) return;
        var target = state.Target;
        var pending = state.Pending;
        ++state.Version;
        state.Running = false;
        state.Pending = false;
        state.Suppress = true;
        viewer.BeginAnimation(AnimatedOffsetProperty, null);
        state.Suppress = false;
        if (state.Subscribed)
        {
            viewer.ScrollChanged -= Scrolled;
            viewer.Unloaded -= ViewerUnloaded;
            viewer.IsVisibleChanged -= VisibilityChanged;
            state.Subscribed = false;
        }
        if (completePending && pending) viewer.ScrollToVerticalOffset(Math.Clamp(target, 0, viewer.ScrollableHeight));
    }

    public static void CancelWithin(DependencyObject root, bool completePending = false)
    {
        if (root is ScrollViewer viewer) Cancel(viewer, completePending);
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) CancelWithin(VisualTreeHelper.GetChild(root, i), completePending);
    }

    private static bool CanScroll(ScrollViewer viewer, int delta) => viewer.VerticalScrollBarVisibility != ScrollBarVisibility.Disabled
        && viewer.ScrollableHeight > .01 && (delta > 0 ? viewer.VerticalOffset > .01 : viewer.VerticalOffset < viewer.ScrollableHeight - .01);
    private static DependencyObject? Parent(DependencyObject element) => element switch
    {
        Visual => VisualTreeHelper.GetParent(element),
        FrameworkContentElement content => ContentOperations.GetParent(content) ?? content.Parent,
        _ => LogicalTreeHelper.GetParent(element)
    };
}
