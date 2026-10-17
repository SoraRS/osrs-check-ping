using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using OsrsWorldPing.Core;
using Forms = System.Windows.Forms;

namespace OsrsWorldPing.Desktop;

public partial class MainWindow : Window
{
    public static readonly DependencyProperty ColumnCountProperty = DependencyProperty.Register(nameof(ColumnCount), typeof(int), typeof(MainWindow), new PropertyMetadata(3));
    public static readonly DependencyProperty FavoriteColumnCountProperty = DependencyProperty.Register(nameof(FavoriteColumnCount), typeof(int), typeof(MainWindow), new PropertyMetadata(5));
    public int FavoriteColumnCount { get => (int)GetValue(FavoriteColumnCountProperty); private set => SetValue(FavoriteColumnCountProperty, value); }
    public int ColumnCount { get => (int)GetValue(ColumnCountProperty); private set => SetValue(ColumnCountProperty, value); }
    public static readonly DependencyProperty IsNavigatingProperty = DependencyProperty.Register(nameof(IsNavigating), typeof(bool), typeof(MainWindow), new PropertyMetadata(false));
    public bool IsNavigating { get => (bool)GetValue(IsNavigatingProperty); private set => SetValue(IsNavigatingProperty, value); }
    private readonly MonitoringEngine _engine;
    private readonly Forms.NotifyIcon _tray;
    private readonly System.Drawing.Icon _trayIcon;
    private readonly Forms.ContextMenuStrip _trayMenu;
    private bool _exitRequested, _closing, _closed, _placementLoaded, _appliedTopmost, _ready;
    private HwndSource? _source;
    private bool _lastShowSettings;
    private int _navigationVersion, _trayTransitionVersion;
    private double _contentTargetHeight, _gearRestAngle;
    public DashboardViewModel Model { get; }

    public MainWindow(MonitoringEngine engine)
    {
        _engine = engine;
        InitializeComponent();
        Model = new(engine, Dispatcher); DataContext = Model;
        Model.PropertyChanged += ModelChanged;
        using var iconStream = Application.GetResourceStream(new Uri("pack://application:,,,/OsrsWorldPing;component/Assets/app.ico")).Stream;
        _trayIcon = new System.Drawing.Icon(iconStream);
        var menu = _trayMenu = new Forms.ContextMenuStrip();
        menu.Items.Add("Show worlds", null, (_, _) => Dispatcher.Invoke(() => RestoreFromTray(false)));
        menu.Items.Add("Settings", null, (_, _) => Dispatcher.Invoke(() => RestoreFromTray(true)));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Dispatcher.Invoke(RequestExit));
        _tray = new Forms.NotifyIcon { Text = "OSRS World Ping", Icon = _trayIcon, ContextMenuStrip = menu };
        _tray.DoubleClick += (_, _) => Dispatcher.Invoke(() => RestoreFromTray(false));
        SizeChanged += (_, _) => UpdateLayoutDensity();
        SettingsTabs.SelectedIndex = 0;
        SettingsContent.Content = ((TabItem)SettingsTabs.Items[0]).Content;
        _ready = true;
    }

    private async void OnLoaded(object sender, RoutedEventArgs args)
    {
        SystemParameters.StaticPropertyChanged += MotionPreferenceChanged;
        // HwndSource invokes the newest hook first. Install after WindowChrome's own hook.
        if (_source is not null)
        {
            _source.RemoveHook(WindowHook); _source.AddHook(WindowHook);
            SetWindowPos(_source.Handle, IntPtr.Zero, 0, 0, 0, 0, 0x0020 | 0x0001 | 0x0002 | 0x0004 | 0x0010);
        }
        UpdateLayoutDensity();
        Model.Refresh();
        try { await _engine.StartAsync(); }
        catch (OperationCanceledException) { }
    }

    private void ModelChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (_ready && !_closed && _lastShowSettings != Model.ShowSettings)
        {
            _lastShowSettings = Model.ShowSettings;
            NavigateSettings();
        }
        if (_closed || !Model.Snapshot.SettingsLoaded) return;
        if (!_placementLoaded)
        {
            _placementLoaded = true;
            RestorePlacement(Model.Snapshot.Settings.Window);
        }
        bool desired = Model.Snapshot.Settings.AlwaysOnTop;
        if (_appliedTopmost != desired)
        {
            _appliedTopmost = desired;
            // Set z-order without activation. Never repeat this on a polling tick.
            SetWindowPos(new WindowInteropHelper(this).Handle, desired ? new IntPtr(-1) : new IntPtr(-2), 0, 0, 0, 0, 0x0010 | 0x0001 | 0x0002);
        }
    }

    private void RestorePlacement(WindowPlacement? placement)
    {
        if (placement is not null)
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            var screen = Forms.Screen.FromRectangle(new System.Drawing.Rectangle(placement.X, placement.Y, placement.Width, placement.Height));
            var area = screen.WorkingArea;
            double availableWidth = area.Width / dpi.DpiScaleX, availableHeight = area.Height / dpi.DpiScaleY;
            MinWidth = Math.Min(420, availableWidth); MinHeight = Math.Min(320, availableHeight);
            double width = Math.Clamp(placement.Width / dpi.DpiScaleX, MinWidth, availableWidth);
            double height = Math.Clamp(placement.Height / dpi.DpiScaleY, MinHeight, availableHeight);
            Width = width; Height = height;
            Left = area.Left / dpi.DpiScaleX + Math.Clamp((placement.X - area.Left) / dpi.DpiScaleX, 0, Math.Max(0, availableWidth - width));
            Top = area.Top / dpi.DpiScaleY + Math.Clamp((placement.Y - area.Top) / dpi.DpiScaleY, 0, Math.Max(0, availableHeight - height));
        }
        // Startup is already maximized in XAML. A late settings read must preserve
        // a user-initiated minimize/restore rather than changing window state.
    }

    private void UpdateLayoutDensity()
    {
        ColumnCount = Math.Clamp((int)(Math.Min(ActualWidth - 48, 1080) / 280), 1, 3);
        bool narrow = ActualWidth < 1400;
        SharedHeader.RowDefinitions[1].Height = new GridLength(narrow ? 42 : 0);
        SharedHeader.RowDefinitions[0].Height = new GridLength(ActualWidth < 740 ? 94 : 72);
        Metrics.MaxWidth = Math.Max(180, ActualWidth - 130);
        Grid.SetColumnSpan(HeaderReadouts, narrow ? 3 : 1);
        FavoriteColumnCount = Math.Clamp((int)(Math.Min(ActualWidth - 48, 1080) / 200), 1, 5);
        double sideWidth = Math.Min(460, Math.Max(0, ActualWidth - 48) / 2);
        SharedHeader.ColumnDefinitions[0].Width = SharedHeader.ColumnDefinitions[2].Width = new GridLength(sideWidth);
        Grid.SetRow(SettingsTabs, narrow ? 1 : 0);
        Grid.SetColumn(SettingsTabs, narrow ? 0 : 1);
        Grid.SetColumnSpan(SettingsTabs, narrow ? 3 : 1);
    }

    private void GearEnter(object sender, MouseEventArgs args) => RotateGear(_gearRestAngle + 90);
    private void GearLeave(object sender, MouseEventArgs args) => RotateGear(_gearRestAngle);
    private void RotateGear(double to)
    {
        if (IsNavigating) return;
        var rotation = (RotateTransform)Gear.RenderTransform;
        if (!Motion.CanAnimate(this)) { rotation.BeginAnimation(RotateTransform.AngleProperty, null); rotation.Angle = to; return; }
        rotation.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(360))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    private void SettingsTabChanged(object sender, SelectionChangedEventArgs args)
    {
        if (!_ready || !ReferenceEquals(args.Source, SettingsTabs) || !Model.ShowSettings) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => { if (!_closed && Model.ShowSettings) NavigateSettings(); }));
    }
    private void SettingsContentSizeChanged(object sender, SizeChangedEventArgs args)
    {
        if (_ready && Model.ShowSettings && !IsNavigating && Math.Abs(SettingsReveal.Height - args.NewSize.Height) > .5) NavigateSettings(resetScroll: false);
    }
    private void NavigateSettings(bool resetScroll = true)
    {
        int version = ++_navigationVersion;
        bool opening = Model.ShowSettings;
        double initialHeight = SettingsReveal.ActualHeight;
        IsNavigating = true;
        SettingsReveal.BeginAnimation(HeightProperty, null); SettingsReveal.Height = initialHeight;
        object? target = opening ? ((TabItem)SettingsTabs.SelectedItem).Content : null;
        object? previous = SettingsContent.Content;
        double previousOpacity = SettingsContent.Opacity;
        double previousOffset = ((TranslateTransform)SettingsContent.RenderTransform).Y;
        bool changing = !ReferenceEquals(previous, target);
        if (changing)
        {
            SettingsContent.Content = null;
            SettingsOutgoing.Content = null;
            SettingsOutgoing.Content = previous;
            SettingsContent.Content = target;
        }
        SettingsTabs.Visibility = Visibility.Visible;
        SettingsContent.Visibility = Visibility.Visible;
        SettingsOutgoing.Visibility = Visibility.Visible;
        SettingsReveal.IsHitTestVisible = opening;
        if (resetScroll) PageScroll.ScrollToTop();
        SettingsContent.Measure(new Size(Math.Max(1, PageFrame.ActualWidth), double.PositiveInfinity));
        _contentTargetHeight = opening ? SettingsContent.DesiredSize.Height : 0;
        if (!Motion.CanAnimate(this)) { StopNavigationMotion(); return; }
        var duration = TimeSpan.FromMilliseconds(600);
        var easing = new CubicEase { EasingMode = EasingMode.EaseInOut };
        var reveal = new DoubleAnimation(initialHeight, _contentTargetHeight, duration) { EasingFunction = easing };
        reveal.Completed += (_, _) => { if (version == _navigationVersion) FinishNavigation(); };
        SettingsReveal.BeginAnimation(HeightProperty, reveal);
        double distance = Math.Max(120, PageScroll.ActualHeight);
        if (changing)
        {
            SettingsOutgoing.BeginAnimation(OpacityProperty, new DoubleAnimation(previousOpacity, 0, TimeSpan.FromMilliseconds(240)));
            ((TranslateTransform)SettingsOutgoing.RenderTransform).BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(previousOffset, distance, TimeSpan.FromMilliseconds(240)) { EasingFunction = easing });
            SettingsContent.BeginAnimation(OpacityProperty, null); SettingsContent.Opacity = 0;
            var incomingOffset = (TranslateTransform)SettingsContent.RenderTransform;
            incomingOffset.BeginAnimation(TranslateTransform.YProperty, null); incomingOffset.Y = -distance;
            SettingsContent.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(420)) { BeginTime = TimeSpan.FromMilliseconds(180) });
            ((TranslateTransform)SettingsContent.RenderTransform).BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(-distance, 0, TimeSpan.FromMilliseconds(420)) { BeginTime = TimeSpan.FromMilliseconds(180), EasingFunction = easing });
        }
        SettingsTabs.BeginAnimation(OpacityProperty, new DoubleAnimation(opening ? 1 : 0, duration));
        ((TranslateTransform)SettingsTabs.RenderTransform).BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(opening ? -8 : 0, opening ? 0 : 8, duration) { EasingFunction = easing });
        var rotation = (RotateTransform)Gear.RenderTransform;
        rotation.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(rotation.Angle, rotation.Angle + (opening ? 240 : -240), duration) { EasingFunction = easing });
        AnimateTrackingControl();
        EmitSparks(opening);
    }
    private void AnimateTrackingControl()
    {
        bool visible = Model.ShowWorldTracking;
        TrackingControlHost.Visibility = Visibility.Visible;
        TrackingControlHost.IsHitTestVisible = visible;
        var offset = (TranslateTransform)TrackingControlHost.RenderTransform;
        double distance = Math.Max(80, PageScroll.ActualHeight / 2);
        TrackingControlHost.BeginAnimation(OpacityProperty, new DoubleAnimation(visible ? 1 : 0, TimeSpan.FromMilliseconds(600)));
        offset.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(visible ? distance : offset.Y, visible ? 0 : distance, TimeSpan.FromMilliseconds(600))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } });
    }
    internal void StopNavigationMotion()
    {
        if (!_ready || _closed) return;
        ++_navigationVersion;
        FinishNavigation();
    }
    private void FinishNavigation()
    {
        bool opening = Model.ShowSettings;
        SettingsReveal.BeginAnimation(HeightProperty, null);
        SettingsReveal.Height = opening ? _contentTargetHeight : 0;
        SettingsContent.BeginAnimation(OpacityProperty, null); SettingsContent.Opacity = opening ? 1 : 0;
        SettingsTabs.BeginAnimation(OpacityProperty, null); SettingsTabs.Opacity = opening ? 1 : 0;
        var headerOffset = (TranslateTransform)SettingsTabs.RenderTransform;
        headerOffset.BeginAnimation(TranslateTransform.YProperty, null); headerOffset.Y = 0;
        foreach (var presenter in new[] { SettingsContent, SettingsOutgoing })
        {
            var offset = (TranslateTransform)presenter.RenderTransform;
            offset.BeginAnimation(TranslateTransform.YProperty, null); offset.Y = 0;
        }
        SettingsOutgoing.BeginAnimation(OpacityProperty, null); SettingsOutgoing.Opacity = 0;
        SettingsOutgoing.Content = null; SettingsOutgoing.Visibility = Visibility.Hidden;
        SettingsContent.Visibility = SettingsTabs.Visibility = opening ? Visibility.Visible : Visibility.Hidden;
        SettingsReveal.IsHitTestVisible = opening;
        TrackingControlHost.BeginAnimation(OpacityProperty, null); TrackingControlHost.Opacity = Model.ShowWorldTracking ? 1 : 0;
        TrackingControlHost.Visibility = Model.ShowWorldTracking ? Visibility.Visible : Visibility.Hidden;
        TrackingControlHost.IsHitTestVisible = Model.ShowWorldTracking;
        var toggleOffset = (TranslateTransform)TrackingControlHost.RenderTransform;
        toggleOffset.BeginAnimation(TranslateTransform.YProperty, null); toggleOffset.Y = 0;
        GearSparks.Children.Clear(); IsNavigating = false;
        var rotation = (RotateTransform)Gear.RenderTransform;
        double angle = rotation.Angle % 360;
        rotation.BeginAnimation(RotateTransform.AngleProperty, null); rotation.Angle = angle;
        _gearRestAngle = angle;
    }
    private void EmitSparks(bool opening)
    {
        GearSparks.Children.Clear();
        for (int i = 0; i < 12; i++)
        {
            var offset = new TranslateTransform();
            var spark = new Ellipse { Width = i % 3 == 0 ? 3 : 2, Height = i % 3 == 0 ? 3 : 2,
                Fill = new SolidColorBrush(i % 3 == 0 ? Color.FromRgb(255, 233, 168) : Color.FromRgb(224, 188, 85)), RenderTransform = offset };
            Canvas.SetLeft(spark, 31); Canvas.SetTop(spark, 52); GearSparks.Children.Add(spark);
            var duration = TimeSpan.FromMilliseconds(420 + i % 4 * 50);
            offset.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, (opening ? -1 : 1) * (35 + i * 5), duration));
            offset.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, 4 + i % 5 * 10, duration) { AccelerationRatio = .6 });
            spark.BeginAnimation(OpacityProperty, new DoubleAnimation(.9, 0, duration));
        }
    }
    private void MotionPreferenceChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(SystemParameters.ClientAreaAnimation) && !SystemParameters.ClientAreaAnimation) StopNavigationMotion();
    }
    private void OpenProfile(object sender, RoutedEventArgs args)
    {
        if (sender is Button { Tag: string address } && Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }

    private async void OnStateChanged(object? sender, EventArgs args)
    {
        int version = ++_trayTransitionVersion;
        if (!_ready || _closed || WindowState != WindowState.Minimized || !Model.Snapshot.Settings.MinimizeToTray) return;
        _tray.Visible = true;
        // Hide only after the native taskbar animation; hiding in WM_SIZE aborts DWM's transition.
        if (SystemParameters.MinimizeAnimation) await Task.Delay(350);
        if (!_closed && version == _trayTransitionVersion && WindowState == WindowState.Minimized) Hide();
    }
    private void HideToTray() { ++_trayTransitionVersion; _tray.Visible = true; Hide(); }
    public void RestoreFromTray(bool settings)
    {
        ++_trayTransitionVersion;
        Show(); if (WindowState == WindowState.Minimized) SystemCommands.RestoreWindow(this);
        _tray.Visible = false; Model.ShowSettings = settings; Activate();
    }
    public void RequestExit() { _exitRequested = true; Close(); }
    private async void OnClosing(object? sender, CancelEventArgs args)
    {
        if (_closed) return;
        args.Cancel = true;
        if (!_exitRequested && Model.Snapshot.Settings.CloseToTray) { HideToTray(); return; }
        if (_closing) return;
        _closing = true; IsEnabled = false;
        try
        {
            var bounds = RestoreBounds;
            var dpi = VisualTreeHelper.GetDpi(this);
            if (_placementLoaded && !bounds.IsEmpty)
                await _engine.SaveWindowPlacementAsync(new((int)(bounds.X * dpi.DpiScaleX), (int)(bounds.Y * dpi.DpiScaleY),
                    (int)(bounds.Width * dpi.DpiScaleX), (int)(bounds.Height * dpi.DpiScaleY), WindowState == WindowState.Maximized));
        }
        catch (OperationCanceledException) { }
        finally
        {
            StopNavigationMotion();
            Model.PropertyChanged -= ModelChanged; Model.Dispose();
            SystemParameters.StaticPropertyChanged -= MotionPreferenceChanged;
            await _engine.DisposeAsync();
            _tray.Visible = false; _tray.Dispose(); _trayMenu.Dispose(); _trayIcon.Dispose();
            _source?.RemoveHook(WindowHook);
            _closed = true; Close();
        }
    }
    private void MinimizeClick(object sender, RoutedEventArgs args) => SystemCommands.MinimizeWindow(this);
    private void MaximizeClick(object sender, RoutedEventArgs args) => ToggleMaximize();
    private void ToggleMaximize() { if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this); else SystemCommands.MaximizeWindow(this); }
    private void CloseClick(object sender, RoutedEventArgs args) => Close();
    private void OpenLink(object sender, RequestNavigateEventArgs args)
    {
        if (args.Uri.Scheme == Uri.UriSchemeHttps) Process.Start(new ProcessStartInfo(args.Uri.AbsoluteUri) { UseShellExecute = true });
        args.Handled = true;
    }
    private void OnSourceInitialized(object? sender, EventArgs args)
    {
        _source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle); _source?.AddHook(WindowHook);
    }
    private IntPtr WindowHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // Standard maximized windows retain an invisible resize border outside rcWork.
        // WindowChrome otherwise paints the client across that border and beneath the taskbar.
        if (message == 0x0083 && wParam != IntPtr.Zero && IsZoomed(hwnd) && GetMonitorInfo(MonitorFromWindow(hwnd, 2), out var monitor))
        {
            Marshal.StructureToPtr(monitor.Work, lParam, false);
            handled = true; return IntPtr.Zero;
        }
        const int maxButton = 9;
        if (_ready && message == 0x0084 && WindowState != WindowState.Minimized && MaximizeButton.IsVisible)
        {
            long packed = lParam.ToInt64();
            var point = new Point((short)(packed & 0xffff), (short)((packed >> 16) & 0xffff));
            var origin = MaximizeButton.PointToScreen(new Point());
            var dpi = VisualTreeHelper.GetDpi(this);
            if (new Rect(origin, new Size(MaximizeButton.ActualWidth * dpi.DpiScaleX, MaximizeButton.ActualHeight * dpi.DpiScaleY)).Contains(point))
            { handled = true; return new IntPtr(maxButton); }
        }
        // HTMAXBUTTON preserves Windows 11's native snap-layout hover target.
        if ((message == 0x00A1 || message == 0x00A2) && wParam.ToInt32() == maxButton)
        { handled = true; if (message == 0x00A2) ToggleMaximize(); }
        return IntPtr.Zero;
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsZoomed(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ReadMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    private static bool GetMonitorInfo(IntPtr handle, out MonitorInfo info)
    { info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() }; return ReadMonitorInfo(handle, ref info); }
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
}
