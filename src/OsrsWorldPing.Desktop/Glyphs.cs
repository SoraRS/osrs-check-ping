using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Input;
using OsrsWorldPing.Core;

namespace OsrsWorldPing.Desktop;

public sealed class FlagGlyph : FrameworkElement
{
    public static readonly DependencyProperty CountryProperty = DependencyProperty.Register(nameof(Country), typeof(string), typeof(FlagGlyph),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));
    public string Country { get => (string)GetValue(CountryProperty); set => SetValue(CountryProperty, value); }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dc.PushTransform(new ScaleTransform(ActualWidth / 30, ActualHeight / 20));
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, 30, 20), 2, 2));
        void Rect(Brush brush, double x, double y, double w, double h) => dc.DrawRectangle(brush, null, new(x, y, w, h));
        var red = new SolidColorBrush(Color.FromRgb(190, 55, 62));
        var blue = new SolidColorBrush(Color.FromRgb(38, 63, 102));
        void Union(double width, double height)
        {
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, width, height)));
            dc.DrawLine(new Pen(Brushes.White, 4), new(0, 0), new(width, height));
            dc.DrawLine(new Pen(Brushes.White, 4), new(0, height), new(width, 0));
            dc.DrawLine(new Pen(red, 1.5), new(0, 0), new(width, height));
            dc.DrawLine(new Pen(red, 1.5), new(0, height), new(width, 0));
            Rect(Brushes.White, width / 2 - 3, 0, 6, height); Rect(Brushes.White, 0, height / 2 - 3, width, 6);
            Rect(red, width / 2 - 1.5, 0, 3, height); Rect(red, 0, height / 2 - 1.5, width, 3);
            dc.Pop();
        }
        switch (Country)
        {
            case "US":
                Rect(Brushes.White, 0, 0, 30, 20);
                for (int i = 0; i < 13; i += 2) Rect(red, 0, i * 20d / 13, 30, 20d / 13);
                Rect(blue, 0, 0, 13, 11);
                for (int y = 0; y < 4; y++) for (int x = 0; x < 5; x++) dc.DrawEllipse(Brushes.White, null, new(1.5 + x * 2.4, 1.5 + y * 2.5), .5, .5);
                break;
            case "GB": Rect(blue, 0, 0, 30, 20); Union(30, 20); break;
            case "AU":
                Rect(blue, 0, 0, 30, 20); Union(15, 10);
                foreach (var point in new[] { new Point(9, 15), new Point(23, 5), new Point(20, 12), new Point(26, 11), new Point(23, 17) })
                    dc.DrawEllipse(Brushes.White, null, point, 1.1, 1.1);
                break;
            case "DE": Rect(Brushes.Black, 0, 0, 30, 7); Rect(red, 0, 7, 30, 6); Rect(Brushes.Goldenrod, 0, 13, 30, 7); break;
            case "BR":
                Rect(Brushes.ForestGreen, 0, 0, 30, 20);
                dc.DrawGeometry(Brushes.Gold, null, Geometry.Parse("M15,2 L28,10 L15,18 L2,10 Z"));
                dc.DrawEllipse(blue, null, new(15, 10), 5, 5);
                dc.DrawLine(new Pen(Brushes.White, 1), new(11, 8), new(19, 11)); break;
            case "JP": Rect(Brushes.White, 0, 0, 30, 20); dc.DrawEllipse(red, null, new(15, 10), 6, 6); break;
            case "SG":
                Rect(Brushes.White, 0, 0, 30, 20); Rect(red, 0, 0, 30, 10);
                dc.DrawEllipse(Brushes.White, null, new(6, 5), 3.4, 3.4); dc.DrawEllipse(red, null, new(7.5, 4.4), 3, 3);
                foreach (var point in new[] { new Point(12, 2), new Point(10, 4), new Point(14, 4), new Point(11, 7), new Point(14, 7) })
                    dc.DrawEllipse(Brushes.White, null, point, .6, .6);
                break;
            case "ZA":
                Rect(red, 0, 0, 30, 10); Rect(blue, 0, 10, 30, 10);
                dc.DrawGeometry(null, new Pen(Brushes.White, 8), Geometry.Parse("M-2,0 L12,10 L32,10 M12,10 L-2,20"));
                dc.DrawGeometry(null, new Pen(Brushes.SeaGreen, 5), Geometry.Parse("M-2,0 L12,10 L32,10 M12,10 L-2,20"));
                dc.DrawGeometry(Brushes.Black, new Pen(Brushes.Gold, 1), Geometry.Parse("M0,4 L8,10 L0,16 Z")); break;
            default:
                Rect(new SolidColorBrush(Color.FromRgb(52, 65, 69)), 0, 0, 30, 20);
                var pen = new Pen(Brushes.LightGray, 1);
                dc.DrawEllipse(null, pen, new(15, 10), 7, 7); dc.DrawEllipse(null, pen, new(15, 10), 3, 7);
                dc.DrawLine(pen, new(8, 10), new(22, 10)); break;
        }
        dc.Pop(); dc.Pop();
    }
}

public sealed class SignalGlyph : FrameworkElement
{
    public static readonly DependencyProperty LevelProperty = DependencyProperty.Register(nameof(Level), typeof(int), typeof(SignalGlyph), new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty GoodProperty = DependencyProperty.Register(nameof(Good), typeof(bool), typeof(SignalGlyph), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
    public int Level { get => (int)GetValue(LevelProperty); set => SetValue(LevelProperty, value); }
    public bool Good { get => (bool)GetValue(GoodProperty); set => SetValue(GoodProperty, value); }
    protected override void OnRender(DrawingContext dc)
    {
        var active = new SolidColorBrush(Good ? Color.FromRgb(148, 204, 177) : Color.FromRgb(221, 173, 101));
        var inactive = new SolidColorBrush(Color.FromRgb(70, 72, 69));
        for (int i = 0; i < 5; i++)
        {
            double h = ActualHeight * (i + 1) / 5;
            dc.DrawRoundedRectangle(i < Level ? active : inactive, null, new(i * ActualWidth / 5, ActualHeight - h, ActualWidth / 7, h), 1, 1);
        }
    }
}

public sealed class ActivityGlyph : FrameworkElement
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(nameof(Kind), typeof(ActivityIcon), typeof(ActivityGlyph), new FrameworkPropertyMetadata(ActivityIcon.World, FrameworkPropertyMetadataOptions.AffectsRender));
    public ActivityIcon Kind { get => (ActivityIcon)GetValue(KindProperty); set => SetValue(KindProperty, value); }
    protected override void OnRender(DrawingContext dc)
    {
        dc.PushTransform(new ScaleTransform(ActualWidth / 24, ActualHeight / 24));
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(183, 164, 128)), 1.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        string? path = Kind switch
        {
            ActivityIcon.Swords => "M4,3 L18,17 M3,7 L7,3 M14,18 L18,14 M16,16 L21,21 M21,3 L7,17 M17,3 L21,7 M6,14 L10,18 M8,16 L3,21",
            ActivityIcon.Shield => "M12,2 L21,6 L20,14 Q18,20 12,23 Q6,20 4,14 L3,6 Z M8,12 L11,15 L17,8",
            ActivityIcon.Trophy => "M7,3 L17,3 L16,12 Q12,18 8,12 Z M7,5 L3,5 L3,10 L8,13 M17,5 L21,5 L21,10 L16,13 M12,16 L12,21 M7,22 L17,22",
            ActivityIcon.Trade => "M3,8 L20,8 L16,4 M20,16 L3,16 L7,20",
            ActivityIcon.Skill => "M3,18 L17,4 L21,8 L7,22 Z M5,5 L10,10 M3,3 L7,3 L7,7 L3,7 Z",
            ActivityIcon.Quest => "M4,3 L16,3 L20,7 L20,21 L4,21 Z M16,3 L16,8 L20,8 M8,12 L16,12 M8,16 L14,16",
            ActivityIcon.Experiment => "M8,3 L16,3 M10,3 L10,10 L4,20 Q4,22 7,22 L17,22 Q20,22 20,20 L14,10 L14,3 M7,16 L17,16",
            _ => null
        };
        if (path is not null) dc.DrawGeometry(null, pen, Geometry.Parse(path));
        else
        {
            dc.DrawEllipse(null, pen, new(12, 12), 9, 9); dc.DrawEllipse(null, pen, new(12, 12), 4, 9);
            dc.DrawLine(pen, new(3, 12), new(21, 12));
        }
        dc.Pop();
    }
}

// Expose cards as read-only groups to assistive technology without introducing selection or focus.
public sealed class WorldCardSurface : System.Windows.Controls.Border
{
    private readonly DropShadowEffect _glow = new() { Color = Color.FromRgb(216, 181, 117), BlurRadius = 14, ShadowDepth = 0, Opacity = 0 };
    private readonly TranslateTransform _lift = new();
    public WorldCardSurface()
    {
        Effect = _glow; RenderTransform = _lift;
        Loaded += (_, _) => SystemParameters.StaticPropertyChanged += OnMotionPreferenceChanged;
        Unloaded += (_, _) => { SystemParameters.StaticPropertyChanged -= OnMotionPreferenceChanged; ResetMotion(); };
    }
    protected override void OnMouseEnter(MouseEventArgs args)
    {
        base.OnMouseEnter(args);
        ResetMotion();
        if (!Motion.CanAnimate(this)) { _glow.Opacity = .24; return; }
        _glow.BeginAnimation(DropShadowEffect.OpacityProperty, new DoubleAnimation(.12, .32, TimeSpan.FromMilliseconds(900))
        { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() });
        _lift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-1.5, TimeSpan.FromMilliseconds(180)));
    }
    protected override void OnMouseLeave(MouseEventArgs args)
    {
        base.OnMouseLeave(args);
        if (!Motion.CanAnimate(this)) { ResetMotion(); return; }
        _glow.BeginAnimation(DropShadowEffect.OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(180)));
        _lift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(180)));
    }
    private void OnMotionPreferenceChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(SystemParameters.ClientAreaAnimation) && !SystemParameters.ClientAreaAnimation) ResetMotion();
    }
    internal void ResetMotion()
    {
        _glow.BeginAnimation(DropShadowEffect.OpacityProperty, null); _glow.Opacity = 0;
        _lift.BeginAnimation(TranslateTransform.YProperty, null); _lift.Y = 0;
    }
    protected override System.Windows.Automation.Peers.AutomationPeer OnCreateAutomationPeer() => new CardPeer(this);
    private sealed class CardPeer(WorldCardSurface owner) : System.Windows.Automation.Peers.FrameworkElementAutomationPeer(owner)
    {
        protected override System.Windows.Automation.Peers.AutomationControlType GetAutomationControlTypeCore() => System.Windows.Automation.Peers.AutomationControlType.Group;
        protected override string GetClassNameCore() => "World card";
    }
}
