using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace McLauncher.App;

public static partial class PageMotion
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(PageMotion), new PropertyMetadata(false, EnabledChanged));
    public static readonly DependencyProperty ReduceMotionProperty = DependencyProperty.RegisterAttached(
        "ReduceMotion", typeof(bool), typeof(PageMotion), new FrameworkPropertyMetadata(
            false, FrameworkPropertyMetadataOptions.Inherits, PreferenceChanged));
    public static readonly DependencyProperty AnimateRefreshProperty = DependencyProperty.RegisterAttached(
        "AnimateRefresh", typeof(bool), typeof(PageMotion), new PropertyMetadata(false));
    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "State", typeof(MotionState), typeof(PageMotion));

    private sealed class MotionState(Transform original)
    {
        public Transform Original { get; } = original;
        public TranslateTransform Offset { get; } = new();
        public bool Running { get; set; }
        public int Generation { get; set; }
        public bool Entered { get; set; }
        public object? Key { get; set; }
        public long Started { get; set; }
        public NotifyCollectionChangedEventHandler? ItemsHandler { get; set; }
        public DispatcherOperation? RefreshOperation { get; set; }
    }

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);
    public static bool GetReduceMotion(DependencyObject element) => (bool)element.GetValue(ReduceMotionProperty);
    public static void SetReduceMotion(DependencyObject element, bool value) => element.SetValue(ReduceMotionProperty, value);
    public static bool GetAnimateRefresh(DependencyObject element) => (bool)element.GetValue(AnimateRefreshProperty);
    public static void SetAnimateRefresh(DependencyObject element, bool value) => element.SetValue(AnimateRefreshProperty, value);

    private static void EnabledChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not FrameworkElement element) return;
        if ((bool)e.NewValue)
        {
            var state = new MotionState(element.RenderTransform);
            var group = new TransformGroup();
            group.Children.Add(state.Original);
            group.Children.Add(state.Offset);
            element.RenderTransform = group;
            element.SetValue(StateProperty, state);
            element.Loaded += Loaded;
            element.Unloaded += Unloaded;
            element.IsVisibleChanged += VisibilityChanged;
            if (element.IsLoaded) Loaded(element, new RoutedEventArgs());
        }
        else
        {
            element.Loaded -= Loaded;
            element.Unloaded -= Unloaded;
            element.IsVisibleChanged -= VisibilityChanged;
            DetachItems(element);
            Stop(element);
            if (element.GetValue(StateProperty) is MotionState state) element.RenderTransform = state.Original;
            element.ClearValue(StateProperty);
        }
    }

    private static void Loaded(object sender, RoutedEventArgs e)
    {
        var element = (FrameworkElement)sender;
        if (element.GetValue(StateProperty) is not MotionState state) return;
        if (element is ListBox list && GetAnimateRefresh(element) && state.ItemsHandler is null)
        {
            state.ItemsHandler = (_, _) =>
            {
                if (state.RefreshOperation is { Status: DispatcherOperationStatus.Pending }) return;
                state.RefreshOperation = element.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
                {
                    state.RefreshOperation = null;
                    Play(element, "items-refresh");
                });
            };
            ((INotifyCollectionChanged)list.Items).CollectionChanged += state.ItemsHandler;
        }
        if (!state.Entered) Play(element, "first-entry");
    }
    private static void Unloaded(object sender, RoutedEventArgs e)
    {
        var element = (FrameworkElement)sender;
        Stop(element); DetachItems(element); ScrollWheelRouting.CancelWithin(element);
    }
    private static void VisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (!(bool)e.NewValue)
        {
            Stop((FrameworkElement)sender);
            ScrollWheelRouting.CancelWithin((FrameworkElement)sender);
        }
    }
    private static void DetachItems(FrameworkElement element)
    {
        if (element.GetValue(StateProperty) is not MotionState state) return;
        state.RefreshOperation?.Abort(); state.RefreshOperation = null;
        if (element is ListBox list && state.ItemsHandler is not null)
            ((INotifyCollectionChanged)list.Items).CollectionChanged -= state.ItemsHandler;
        state.ItemsHandler = null;
    }
    private static void PreferenceChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is FrameworkElement header && header.GetValue(HeaderStateProperty) is HeaderState hs)
            SetHeaderTarget(header, hs, hs.Compact, true);
        if ((bool)e.NewValue && sender is FrameworkElement element)
        {
            Stop(element);
            if (GetIsEnabled(element) || ScrollWheelRouting.GetIsEnabled(element)) ScrollWheelRouting.CancelWithin(element, true);
        }
    }

    public static void Play(FrameworkElement element, object? transitionKey = null)
    {
        if (!GetIsEnabled(element) || element.GetValue(StateProperty) is not MotionState state) return;
        if (GetReduceMotion(element) || !SystemParameters.ClientAreaAnimation || !element.IsLoaded || !element.IsVisible)
        {
            Stop(element);
            return;
        }
        if (state.Running && ((transitionKey is not null && Equals(state.Key, transitionKey))
            || (transitionKey is null && Environment.TickCount64 - state.Started < 50))) return;
        for (var parent = VisualTreeHelper.GetParent(element); parent is not null; parent = VisualTreeHelper.GetParent(parent))
            if (parent.GetValue(StateProperty) is MotionState { Running: true }) { state.Entered = true; Stop(element); return; }
        ScrollWheelRouting.CancelWithin(element);
        state.Entered = true;
        state.Key = transitionKey;
        state.Started = Environment.TickCount64;
        var opacity = (double)element.GetAnimationBaseValue(UIElement.OpacityProperty);
        var fromOpacity = state.Running ? element.Opacity : opacity * .94;
        var fromOffset = state.Running ? state.Offset.Y : 10;
        var generation = ++state.Generation;
        state.Running = true;
        var arrival = new DoubleAnimation(fromOffset, 0, TimeSpan.FromMilliseconds(260))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop
        };
        arrival.Completed += (_, _) =>
        {
            if (state.Generation == generation) Stop(element);
        };
        element.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(fromOpacity, opacity, TimeSpan.FromMilliseconds(240))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop
            }, HandoffBehavior.SnapshotAndReplace);
        state.Offset.BeginAnimation(TranslateTransform.YProperty, arrival, HandoffBehavior.SnapshotAndReplace);
    }

    private static void Stop(FrameworkElement element)
    {
        if (element.GetValue(StateProperty) is not MotionState state) return;
        ++state.Generation;
        state.Running = false;
        element.BeginAnimation(UIElement.OpacityProperty, null);
        state.Offset.BeginAnimation(TranslateTransform.YProperty, null);
        state.Offset.Y = 0;
    }
}
