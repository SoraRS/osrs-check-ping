using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace OsrsWorldPing.Desktop;

public static class Motion
{
    // Inherited switch also lets deterministic UI checks exercise reduced motion without changing the OS.
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached("Enabled", typeof(bool), typeof(Motion), new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.Inherits, MotionPreferenceChanged));
    public static bool GetEnabled(DependencyObject element) => (bool)element.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject element, bool value) => element.SetValue(EnabledProperty, value);
    private static void MotionPreferenceChanged(DependencyObject element, DependencyPropertyChangedEventArgs args)
    {
        if ((bool)args.NewValue) return;
        if (element is WorldCardSurface surface) surface.ResetMotion();
        if (element is MainWindow window) window.StopNavigationMotion();
        if (element is FrameworkElement { RenderTransform: ScaleTransform scale })
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null); scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            scale.ScaleX = scale.ScaleY = 1;
        }
    }
    public static bool CanAnimate(DependencyObject element) => GetEnabled(element) && SystemParameters.ClientAreaAnimation;

    public static readonly DependencyProperty HoverScaleProperty = DependencyProperty.RegisterAttached("HoverScale", typeof(bool), typeof(Motion), new PropertyMetadata(false, AttachHover));
    public static bool GetHoverScale(DependencyObject element) => (bool)element.GetValue(HoverScaleProperty);
    public static void SetHoverScale(DependencyObject element, bool value) => element.SetValue(HoverScaleProperty, value);
    private static void AttachHover(DependencyObject element, DependencyPropertyChangedEventArgs args)
    {
        if (element is not FrameworkElement view) return;
        if ((bool)args.OldValue)
        {
            view.MouseEnter -= Hover; view.MouseLeave -= Hover;
            if (view is TabItem tab) { tab.Selected -= TabSelectionScale; tab.Unselected -= TabSelectionScale; }
        }
        if ((bool)args.NewValue)
        {
            view.MouseEnter += Hover; view.MouseLeave += Hover;
            if (view is TabItem tab) { tab.Selected += TabSelectionScale; tab.Unselected += TabSelectionScale; }
        }
    }
    private static void Hover(object sender, System.Windows.Input.MouseEventArgs args) => UpdateScale((FrameworkElement)sender);
    private static void TabSelectionScale(object sender, RoutedEventArgs args) => UpdateScale((FrameworkElement)sender);
    private static void UpdateScale(FrameworkElement view)
    {
        if (view.RenderTransform is not ScaleTransform scale) { scale = new ScaleTransform(1, 1); view.RenderTransformOrigin = new Point(.5, .5); view.RenderTransform = scale; }
        double to = (view.IsMouseOver || view is TabItem { IsSelected: true }) && CanAnimate(view) ? 1.06 : 1;
        if (!CanAnimate(view)) { scale.BeginAnimation(ScaleTransform.ScaleXProperty, null); scale.BeginAnimation(ScaleTransform.ScaleYProperty, null); scale.ScaleX = scale.ScaleY = 1; return; }
        var animation = new DoubleAnimation(to, TimeSpan.FromMilliseconds(150)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, animation); scale.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
    }

    public static readonly DependencyProperty PopOnCheckProperty = DependencyProperty.RegisterAttached("PopOnCheck", typeof(bool), typeof(Motion), new PropertyMetadata(false, AttachPop));
    public static bool GetPopOnCheck(DependencyObject element) => (bool)element.GetValue(PopOnCheckProperty);
    public static void SetPopOnCheck(DependencyObject element, bool value) => element.SetValue(PopOnCheckProperty, value);
    private static void AttachPop(DependencyObject element, DependencyPropertyChangedEventArgs args)
    {
        if (element is not ToggleButton button) return;
        if ((bool)args.OldValue) button.Checked -= Pop;
        if ((bool)args.NewValue) button.Checked += Pop;
    }
    private static void Pop(object sender, RoutedEventArgs args)
    {
        if (sender is not ToggleButton { IsLoaded: true } button || !CanAnimate(button)) return;
        var scale = new ScaleTransform(1, 1);
        button.RenderTransformOrigin = new Point(.5, .5); button.RenderTransform = scale;
        var animation = new DoubleAnimation(1, 1.16, TimeSpan.FromMilliseconds(130))
        { AutoReverse = true, FillBehavior = FillBehavior.Stop, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, animation); scale.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
    }
}
