using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Navigation;
using OsrsCheckPing.Core;
using Forms = System.Windows.Forms;

namespace OsrsCheckPing.Desktop;

public partial class MainWindow : Window
{
    public static readonly DependencyProperty ColumnCountProperty = DependencyProperty.Register(nameof(ColumnCount), typeof(int), typeof(MainWindow), new PropertyMetadata(5));
    public int ColumnCount { get => (int)GetValue(ColumnCountProperty); private set => SetValue(ColumnCountProperty, value); }
    private readonly MonitoringEngine _engine;
    private readonly Forms.NotifyIcon _tray;
    private readonly System.Drawing.Icon _trayIcon;
    private readonly Forms.ContextMenuStrip _trayMenu;
    private bool _exitRequested, _closing, _closed, _placementLoaded, _appliedTopmost, _ready;
    private HwndSource? _source;
    public DashboardViewModel Model { get; }

    public MainWindow(MonitoringEngine engine)
    {
        _engine = engine;
        InitializeComponent();
        Model = new(engine, Dispatcher); DataContext = Model;
        Model.PropertyChanged += ModelChanged;
        using var iconStream = Application.GetResourceStream(new Uri("pack://application:,,,/OsrsCheckPing;component/Assets/app.ico")).Stream;
        _trayIcon = new System.Drawing.Icon(iconStream);
        var menu = _trayMenu = new Forms.ContextMenuStrip();
        menu.Items.Add("Show worlds", null, (_, _) => Dispatcher.Invoke(() => RestoreFromTray(false)));
        menu.Items.Add("Settings", null, (_, _) => Dispatcher.Invoke(() => RestoreFromTray(true)));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Dispatcher.Invoke(RequestExit));
        _tray = new Forms.NotifyIcon { Text = "OSRS Check Ping", Icon = _trayIcon, ContextMenuStrip = menu };
        _tray.DoubleClick += (_, _) => Dispatcher.Invoke(() => RestoreFromTray(false));
        SizeChanged += (_, _) => UpdateLayoutDensity();
        _ready = true;
    }

    private async void OnLoaded(object sender, RoutedEventArgs args)
    {
        UpdateLayoutDensity();
        Model.Refresh();
        try { await _engine.StartAsync(); }
        catch (OperationCanceledException) { }
    }

    private void ModelChanged(object? sender, PropertyChangedEventArgs args)
    {
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
            double width = Math.Clamp(placement.Width / dpi.DpiScaleX, MinWidth, Math.Max(MinWidth, area.Width / dpi.DpiScaleX));
            double height = Math.Clamp(placement.Height / dpi.DpiScaleY, MinHeight, Math.Max(MinHeight, area.Height / dpi.DpiScaleY));
            Width = width; Height = height;
            Left = Math.Clamp(placement.X / dpi.DpiScaleX, area.Left / dpi.DpiScaleX, area.Right / dpi.DpiScaleX - width);
            Top = Math.Clamp(placement.Y / dpi.DpiScaleY, area.Top / dpi.DpiScaleY, area.Bottom / dpi.DpiScaleY - height);
        }
        WindowState = WindowState.Maximized;
    }

    private void UpdateLayoutDensity()
    {
        ColumnCount = Math.Clamp((int)((ActualWidth - 64) / 290), 1, 5);
        bool narrow = ActualWidth < 850;
        System.Windows.Controls.Grid.SetRow(Metrics, narrow ? 1 : 0);
        System.Windows.Controls.Grid.SetColumn(Metrics, narrow ? 0 : 1);
        System.Windows.Controls.Grid.SetColumnSpan(Metrics, narrow ? 3 : 1);
        Metrics.Margin = narrow ? new Thickness(6, 8, 0, 8) : new Thickness(24, 0, 24, 0);
        DashboardHeaderRows.Height = new GridLength(narrow ? 154 : 96);
    }

    private void GearEnter(object sender, MouseEventArgs args) => RotateGear(90);
    private void GearLeave(object sender, MouseEventArgs args) => RotateGear(0);
    private void RotateGear(double to)
    {
        var rotation = (RotateTransform)Gear.RenderTransform;
        if (!SystemParameters.ClientAreaAnimation) { rotation.BeginAnimation(RotateTransform.AngleProperty, null); rotation.Angle = to; return; }
        rotation.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(240))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    private void OnStateChanged(object? sender, EventArgs args)
    {
        if (_ready && !_closed && WindowState == WindowState.Minimized && Model.Snapshot.Settings.MinimizeToTray) HideToTray();
    }
    private void HideToTray() { _tray.Visible = true; Hide(); }
    public void RestoreFromTray(bool settings)
    {
        Show(); if (WindowState == WindowState.Minimized) WindowState = WindowState.Maximized;
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
            Model.PropertyChanged -= ModelChanged; Model.Dispose();
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
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
}
