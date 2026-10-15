using System.Collections.Specialized;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Effects;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using OsrsCheckPing.Core;
using OsrsCheckPing.Core.Tests;
using OsrsCheckPing.Desktop;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace OsrsCheckPing.Desktop.Tests;

public sealed class DesktopTests
{
    private static readonly Lazy<Task<Dispatcher>> Ui = new(() =>
    {
        var ready = new TaskCompletionSource<Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/OsrsCheckPing;component/Theme.xaml", UriKind.Relative) });
            ready.SetResult(app.Dispatcher); app.Run();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); return ready.Task;
    });
    private static async Task OnUiThreadAsync(Func<Task> test) =>
        await (await Ui.Value).InvokeAsync(test).Task.Unwrap().WaitAsync(TimeSpan.FromSeconds(90));
    private static async Task UntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(12);
        while (!condition()) { if (DateTime.UtcNow > deadline) throw new TimeoutException("WPF condition was not met"); await Task.Delay(30); }
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
    }
    private static async Task ResizeAsync(MainWindow window, double width, double height)
    {
        window.WindowState = WindowState.Normal;
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        window.Width = width; window.Height = height;
        await UntilAsync(() => Math.Abs(window.ActualWidth - width) < 1 && Math.Abs(window.ActualHeight - height) < 1);
        window.UpdateLayout();
    }

    private static async Task ClickAsync(ToggleButton element)
    {
        element.UpdateLayout();
        Assert.True(element.IsVisible && element.IsEnabled, $"{element.Name} must be available for a native click.");
        var at = element.PointToScreen(new Point(element.ActualWidth / 2, element.ActualHeight / 2));
        Assert.True(SetCursorPos((int)at.X, (int)at.Y));
        await UntilAsync(() => element.IsMouseOver); // Let WPF process the pointer move before sending button input.
        Input[] input = [new() { Data = new() { Mouse = new() { Flags = 0x0002 } } }, new() { Data = new() { Mouse = new() { Flags = 0x0004 } } }];
        int clicks = 0;
        RoutedEventHandler clicked = (_, _) => clicks++;
        element.Click += clicked;
        try
        {
            Assert.Equal(2u, SendInput(2, input, Marshal.SizeOf<Input>()));
            await Task.Delay(80); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.True(clicks == 1, $"Native click at {at} must reach {element.Name}; received {clicks} clicks.");
        }
        finally { element.Click -= clicked; }
    }

    private static Harness Samples()
    {
        var h = new Harness();
        h.Store.Settings = new AppSettings { FavoriteWorldIds = [301, 302, 303, 304, 305] };
        string[] activities = ["2200 skill total", "Wintertodt", "Tempoross", "Forestry", "Guardians of the Rift", "Trading", "Agility Training", "Blast Furnace", "Fishing Trawler", "PvP", "A long activity description that must remain readable", "Nex FFA", "Nightmare of Ashihama", "Mastering Mixology", "Zalcano", "Volcanic Mine", "Tombs of Amascut", "House Party, Gilded Altar", "Sorceress's Garden", "Salvaging"];
        h.Catalogue.Worlds = Enumerable.Range(301, 40).Select(id => new WorldInfo(id,
            id == 301 ? (uint)(WorldFlags.Members | WorldFlags.SkillTotal) : id == 310 ? (uint)(WorldFlags.Members | WorldFlags.Pvp) : (uint)(id % 2),
            $"world-{id}.runescape.com", activities[(id - 301) % activities.Length],
            (id % 4) switch { 0 => 0, 1 => 1, 2 => 3, _ => 7 }, 100)).ToArray();
        h.Probe.Handler = (host, _) => Task.FromResult(new ProbeResult(ProbeStatus.Success, int.Parse(host.Split('.')[0].AsSpan(6)) - 300));
        return h;
    }
    private static async Task CloseAsync(MainWindow window) { window.RequestExit(); await UntilAsync(() => !Application.Current.Windows.Cast<Window>().Contains(window)); }
    private static IEnumerable<T> Children<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T found) yield return found;
            foreach (var descendant in Children<T>(child)) yield return descendant;
        }
    }
    private static void Preview(FrameworkElement element, string name, double scale = 1)
    {
        element.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth * scale), (int)Math.Ceiling(element.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        string directory = Path.Combine(AppContext.BaseDirectory, "ui-preview"); Directory.CreateDirectory(directory);
        using var stream = File.Create(Path.Combine(directory, name)); encoder.Save(stream);
    }

    [Fact]
    public Task DashboardFitsTwentyPlusFiveAndSettingsStayInsideOneWindow() => OnUiThreadAsync(async () =>
    {
        var h = Samples(); var main = new MainWindow(h.Engine); main.Show();
        try
        {
            await UntilAsync(() => main.Model.Tracked.Count == 20);
            Assert.Equal(WindowState.Maximized, main.WindowState);
            await ResizeAsync(main, 1920, 1040);
            Assert.Equal(5, main.ColumnCount);
            Assert.Equal(5, main.Model.Favorites.Count); Assert.Equal(25, main.Model.Snapshot.LiveCount);
            var scroll = (ScrollViewer)main.FindName("DashboardScroll");
            Preview(main, "dashboard-1080p.png");
            Assert.True(scroll.ScrollableHeight < 1, $"All 25 cards must fit at 1080p: overflow {scroll.ScrollableHeight}");
            Assert.True(scroll.ExtentHeight <= 420, "Twenty-five compact cards should leave most of the landscape visible.");
            var tile = Children<WorldTile>(main).First(view => view.IsVisible && view.DataContext is WorldCardModel { Id: 301 });
            Assert.InRange(tile.ActualHeight, 60, 72);
            var number = (TextBlock)tile.FindName("WorldNumber"); var ping = (TextBlock)tile.FindName("LatencyValue"); var bars = (SignalGlyph)tile.FindName("ConnectionBars");
            var np = number.TranslatePoint(new Point(), tile); var pp = ping.TranslatePoint(new Point(), tile); var bp = bars.TranslatePoint(new Point(), tile);
            Assert.True(Math.Abs(np.Y - pp.Y) < 5 && Math.Abs(pp.Y - bp.Y) < 5, "World number, latency, and bars belong on one line.");
            Assert.InRange(pp.X - (np.X + number.ActualWidth), 0, 12);
            Assert.Empty(Children<ListBox>(main)); Assert.Empty(Children<DataGrid>(main));
            main.Model.OpenSettings.Execute(null); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.True(main.Model.ShowSettings); Assert.Single(Application.Current.Windows.Cast<MainWindow>());
            var tabs = (TabControl)main.FindName("SettingsTabs"); Assert.Equal(3, tabs.Items.Count);
            var sheet = (Border)main.FindName("SettingsSurface");
            Assert.Equal(0, ((SolidColorBrush)sheet.Background).Color.A);
            var trackingCards = (ItemsControl)main.FindName("TrackingCards");
            main.UpdateLayout();
            Assert.True(trackingCards.ActualHeight <= 350, "Tracking settings must use a compact grid, not one world per line.");
            Assert.Equal(5, Children<System.Windows.Controls.Primitives.UniformGrid>(trackingCards).Single().Columns);
            for (int tab = 0; tab < 3; tab++)
            {
                tabs.SelectedIndex = tab; main.UpdateLayout();
                if (tab > 0)
                {
                    var content = (Border)main.FindName(tab == 1 ? "ApplicationSheet" : "AboutSheet");
                    Assert.InRange(content.ActualHeight, 400, 650); // Local sheets end at their content instead of covering unused scenery.
                }
                Preview(main, $"settings-{tab}-1080p.png");
            }
            tabs.SelectedIndex = 0;
            Assert.Equal(25, main.Model.WorldView.Cast<WorldCardModel>().Count());
            int probes = h.Probe.Hosts.Count; main.Model.BrowseHidden = true;
            Assert.Equal(40, main.Model.WorldView.Cast<WorldCardModel>().Count()); Assert.Equal(probes, h.Probe.Hosts.Count);
            main.UpdateLayout();
            var pin = Children<ToggleButton>(main).Single(box => box.IsVisible && box.DataContext is WorldCardModel { Id: 306 } && box.Name == "FavoriteToggle");
            await ClickAsync(pin);
            await UntilAsync(() => pin.IsChecked == false);
            Assert.Equal(5, main.Model.Favorites.Count);
            Assert.Contains("up to five", main.Model.Feedback);
            var locked = Children<ToggleButton>(main).Single(box => box.IsVisible && box.DataContext is WorldCardModel { Id: 301 } && box.Name == "TrackingToggle");
            Assert.True(locked.IsChecked); Assert.False(locked.IsEnabled);
            main.Model.Search = "WorldDoesNotExist"; Assert.Empty(main.Model.WorldView.Cast<WorldCardModel>()); main.Model.Search = "";
            var search = (TextBox)main.FindName("WorldSearch"); search.Focus();
            await h.Engine.CheckTrackedWorldsAsync(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.Same(search, Keyboard.FocusedElement);
            Assert.True(search.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)));
            await ResizeAsync(main, 1920, 720);
            var trackingScroll = (ScrollViewer)main.FindName("TrackingScroll");
            trackingScroll.ScrollToVerticalOffset(200); main.UpdateLayout();
            double settingsOffset = trackingScroll.VerticalOffset;
            Assert.True(settingsOffset > 0, "Settings scroll preservation must be tested with a scrolled viewport.");
            await h.Engine.CheckTrackedWorldsAsync(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.Equal(settingsOffset, trackingScroll.VerticalOffset);
            main.Model.Cutoff = "80"; main.Model.AlwaysOnTop = true; main.Model.ApplySettings.Execute(null);
            await UntilAsync(() => h.Engine.GetSnapshot().Settings.LatencyCutoffMs == 80);
            Assert.True(h.Store.Settings.AlwaysOnTop);
            main.Model.BackToWorlds.Execute(null); Assert.False(main.Model.ShowSettings);
            foreach (var (width, height, scale) in new[] { (1366d, 728d, 1d), (1280d, 693d, 1.5), (960d, 520d, 2d), (683d, 384d, 2d), (480d, 640d, 1d) })
            {
                await ResizeAsync(main, width, height);
                Preview(main, $"dashboard-{width}-{scale}.png", scale);
                Assert.InRange(main.ColumnCount, 1, 5);
                main.Model.ShowSettings = true;
                for (int tab = 0; tab < 3; tab++)
                {
                    tabs.SelectedIndex = tab; main.UpdateLayout();
                    var pageScroll = tab == 0 ? trackingScroll : (ScrollViewer)((TabItem)tabs.Items[tab]).Content;
                    pageScroll.ScrollToTop(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); main.UpdateLayout();
                    Preview(main, $"settings-{tab}-{width}-{scale}.png", scale);
                }
                main.Model.ShowSettings = false;
            }
        }
        finally { await CloseAsync(main); await h.DisposeAsync(); }
    });

    [Fact]
    public Task TrackingGridFadesInactiveCardsUsesRealSpritesAndHonorsReducedMotion() => OnUiThreadAsync(async () =>
    {
        var h = Samples(); var main = new MainWindow(h.Engine); main.Show();
        try
        {
            await UntilAsync(() => main.Model.Tracked.Count == 20);
            await ResizeAsync(main, 1920, 1040);
            main.Model.ShowSettings = true; main.Model.BrowseHidden = true;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); main.UpdateLayout();
            foreach (var uri in main.Model.AllWorlds.Select(world => world.ActivitySprite).OfType<string>().Distinct())
            {
                using var stream = Application.GetResourceStream(new Uri(uri)).Stream;
                var frame = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                Assert.InRange(frame.PixelWidth, 1, 32); Assert.InRange(frame.PixelHeight, 1, 32);
            }
            WorldTile Tile(int id) => Children<WorldTile>(main).Single(view => view.IsVisible && view.Editable && view.DataContext is WorldCardModel world && world.Id == id);
            var total = (System.Windows.Controls.Image)Tile(301).FindName("ActivitySprite");
            Assert.Equal(23, ((BitmapSource)total.Source).PixelWidth); // Native experience/total-level bar graph.
            var inactive = Tile(340); var surface = (WorldCardSurface)inactive.FindName("Surface");
            SetCursorPos(1, 1); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.InRange(surface.Opacity, .4, .5);
            Assert.True(((SolidColorBrush)surface.Background).Color.A < 128, "Individual tiles must let the landscape through.");
            var at = surface.PointToScreen(new Point(surface.ActualWidth / 2, surface.ActualHeight / 2));
            SetCursorPos((int)at.X, (int)at.Y);
            await UntilAsync(() => surface.IsMouseOver && surface.Opacity > .8);
            if (Motion.CanAnimate(surface))
            {
                var glow = (DropShadowEffect)surface.Effect;
                Assert.True(glow.HasAnimatedProperties);
                double initial = glow.Opacity; await Task.Delay(260);
                Assert.True(Math.Abs(initial - glow.Opacity) > .005, "The hover glow must actually animate, not just retain a clock.");
            }
            Preview(main, "tracking-hover-glow.png");
            Motion.SetEnabled(main, false); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.False(((DropShadowEffect)surface.Effect).HasAnimatedProperties);
            SetCursorPos(1, 1); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var favorite = (ToggleButton)Tile(301).FindName("FavoriteToggle");
            var ordinary = (ToggleButton)Tile(306).FindName("FavoriteToggle");
            Assert.True(favorite.IsChecked); Assert.False(ordinary.IsChecked);
            Assert.Equal(Color.FromRgb(242, 125, 140), ((SolidColorBrush)favorite.Foreground).Color);
            Assert.True(ordinary.Opacity < favorite.Opacity);
            await ClickAsync(favorite);
            await UntilAsync(() => !main.Model.AllWorlds.Single(w => w.Id == 301).IsFavorite);
            await ClickAsync(ordinary);
            await UntilAsync(() => main.Model.AllWorlds.Single(w => w.Id == 306).IsFavorite);
            Assert.Equal(5, main.Model.Favorites.Count);
            var before = Tile(340);
            await ClickAsync((ToggleButton)before.FindName("TrackingToggle"));
            await UntilAsync(() => main.Model.AllWorlds.Single(w => w.Id == 340).IsTracked);
            Assert.Same(before, Tile(340)); // Browsing all worlds must not rebuild tiles when tracking changes.
            Assert.Equal(1, ((WorldCardSurface)Tile(340).FindName("Surface")).Opacity);
            var track = (ToggleButton)Tile(340).FindName("TrackingToggle");
            Assert.True(track.IsChecked);
            Preview(main, "tracking-browse-compact.png");
        }
        catch { Preview(main, "tracking-interaction-failure.png"); throw; }
        finally { SetCursorPos(1, 1); await CloseAsync(main); await h.DisposeAsync(); }
    });

    [Fact]
    public Task ReadingsKeepCardIdentityOrderScrollAndFocusWhileBackgrounded() => OnUiThreadAsync(async () =>
    {
        var h = Samples(); var main = new MainWindow(h.Engine); var sentinel = new Window { Title = "Foreground sentinel", Width = 350, Height = 180 };
        main.Show();
        try
        {
            await UntilAsync(() => main.Model.Tracked.Count == 20);
            main.WindowState = WindowState.Normal; main.Width = 720; main.Height = 360; main.UpdateLayout();
            var scroll = (ScrollViewer)main.FindName("DashboardScroll"); scroll.ScrollToVerticalOffset(240); main.UpdateLayout();
            Assert.True(scroll.ScrollableHeight > 0, "Use a viewport that actually exercises scrolling.");
            double offset = scroll.VerticalOffset; var cards = main.Model.Tracked.ToArray(); int changes = 0;
            main.Model.Tracked.CollectionChanged += (_, _) => changes++;
            sentinel.Show(); sentinel.Activate();
            var foreground = new WindowInteropHelper(sentinel).Handle;
            Assert.Equal(foreground, GetForegroundWindow());
            h.Probe.Return(new ProbeResult(ProbeStatus.Success, 29));
            for (int i = 0; i < 3; i++)
            {
                await h.Engine.CheckTrackedWorldsAsync(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Assert.Equal(foreground, GetForegroundWindow()); Assert.Equal(offset, scroll.VerticalOffset);
                Assert.Equal(cards, main.Model.Tracked); Assert.Equal(0, changes);
            }
            await h.Engine.RecheckHiddenWorldsAsync(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.Equal(foreground, GetForegroundWindow());
            await h.Engine.UpdatePreferencesAsync(30, 5, true, false, false);
            await UntilAsync(() => (GetWindowLongPtr(new WindowInteropHelper(main).Handle, -20).ToInt64() & 8) != 0);
            Assert.Equal(foreground, GetForegroundWindow());
            await h.Engine.UpdatePreferencesAsync(30, 5, false, true, true);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            main.WindowState = WindowState.Minimized; await UntilAsync(() => !main.IsVisible);
            sentinel.Activate(); await h.Engine.CheckTrackedWorldsAsync(); await h.Engine.RecheckHiddenWorldsAsync();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.False(main.IsVisible); Assert.Equal(WindowState.Minimized, main.WindowState); Assert.Equal(foreground, GetForegroundWindow());
            main.RestoreFromTray(true); Assert.True(main.IsVisible); Assert.True(main.Model.ShowSettings);
            main.Close(); Assert.False(main.IsVisible); // close-to-tray
        }
        finally { sentinel.Close(); await CloseAsync(main); await h.DisposeAsync(); }
    });

    [Fact]
    public Task ExistingOffscreenBoundsAreClampedAndStartupStillMaximizes() => OnUiThreadAsync(async () =>
    {
        var h = new Harness(); h.Store.Settings = new AppSettings { Window = new(-50000, -50000, 1000, 600, false) };
        var main = new MainWindow(h.Engine); main.Show();
        try
        {
            await UntilAsync(() => main.Model.Snapshot.SettingsLoaded);
            Assert.Equal(WindowState.Maximized, main.WindowState);
            main.WindowState = WindowState.Normal; main.UpdateLayout(); Assert.True(main.Left > -50000); Assert.True(main.Top > -50000);
            Assert.NotNull(main.Icon);
        }
        finally { await CloseAsync(main); await h.DisposeAsync(); }
        Assert.NotNull(h.Store.Settings.Window);
    });

    [Fact]
    public Task OfflineAndFallbackStatesKeepSettingsAccessible() => OnUiThreadAsync(async () =>
    {
        var h = new Harness(); h.Store.Settings = new AppSettings { FavoriteWorldIds = [999] };
        h.Catalogue.Error = new HttpRequestException("Offline fixture");
        var main = new MainWindow(h.Engine); main.Show();
        try
        {
            await UntilAsync(() => main.Model.HasNotice && !main.Model.Snapshot.IsScanning);
            Assert.Equal("Unavailable", main.Model.Favorites.Single().Latency);
            Assert.False(main.Model.HasTracked);
            Assert.Equal("World catalogue unavailable", main.Model.EmptyTitle);
            Preview(main, "offline-no-cache.png");
            main.Model.OpenSettings.Execute(null); Assert.True(main.Model.ShowSettings);
            h.Catalogue.Error = null;
            h.Catalogue.Worlds = [new(301, (uint)(WorldFlags.Members | WorldFlags.HighRisk), "supplied.example", "High Risk", 0, 10),
                new(302, 0, "unknown.example", "A new activity", 250, 10),
                new(303, (uint)(WorldFlags.Pvp | WorldFlags.Seasonal), "league.example", "Leagues", 3, 10)];
            h.Probe.Handler = (host, _) => Task.FromResult(host == "supplied.example" ? new ProbeResult(ProbeStatus.TimedOut) : new ProbeResult(ProbeStatus.Success, 10));
            await h.Engine.RecheckAsync(); await h.Engine.SetFavoriteAsync(301, true);
            await UntilAsync(() => main.Model.Favorites.Count == 2);
            Assert.Equal("Timeout", main.Model.Favorites.Single(w => w.Id == 301).Latency);
            Assert.Equal("", main.Model.AllWorlds.Single(w => w.Id == 302).CountryCode);
            Assert.False(main.Model.AllWorlds.Single(w => w.Id == 303).IsDangerous);
            main.Model.ShowSettings = false; main.UpdateLayout(); Preview(main, "dashboard-failure-fallbacks.png");
            h.Catalogue.Error = new HttpRequestException("Offline again"); await h.Engine.RecheckAsync();
            await UntilAsync(() => main.Model.HasNotice);
            main.Model.ShowSettings = true; main.UpdateLayout(); Preview(main, "cached-catalogue-notice.png");
            Assert.Contains("saved world catalogue", main.Model.Snapshot.Notice);
        }
        finally { await CloseAsync(main); await h.DisposeAsync(); }
    });

    [Fact]
    public Task ExitDuringDiscoveryCancelsWorkAndClosesTheWindow() => OnUiThreadAsync(async () =>
    {
        var h = new Harness(); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Probe.Handler = async (_, token) =>
        {
            entered.TrySetResult(); await Task.Delay(Timeout.Infinite, token); return new ProbeResult(ProbeStatus.Error);
        };
        var main = new MainWindow(h.Engine); main.Show();
        try { await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); Assert.True(h.Engine.GetSnapshot().IsScanning); }
        finally { await CloseAsync(main); await h.DisposeAsync(); }
    });

    [Fact]
    public async Task NativeIcmpReachesWindowsLoopbackWithoutElevation()
    {
        var result = await new IcmpLatencyProbe().ProbeAsync("127.0.0.1", CancellationToken.None);
        Assert.Equal(ProbeStatus.Success, result.Status);
    }
    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public InputUnion Data; }
    [StructLayout(LayoutKind.Explicit)] private struct InputUnion { [FieldOffset(0)] public MouseInput Mouse; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseInput { public int X, Y; public uint Data, Flags, Time; public UIntPtr Extra; }
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] input, int size);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
}
