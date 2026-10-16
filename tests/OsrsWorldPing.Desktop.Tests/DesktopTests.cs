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
using OsrsWorldPing.Core;
using OsrsWorldPing.Core.Tests;
using OsrsWorldPing.Desktop;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace OsrsWorldPing.Desktop.Tests;

public sealed class DesktopTests
{
    private static readonly Lazy<Task<Dispatcher>> Ui = new(() =>
    {
        var ready = new TaskCompletionSource<Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/OsrsWorldPing;component/Theme.xaml", UriKind.Relative) });
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
        // Restored bounds can be centered; a full-desktop test window must start at the work-area origin
        // so native pointer input can reach its last column, not just an off-screen rendered preview.
        window.Left = SystemParameters.WorkArea.Left; window.Top = SystemParameters.WorkArea.Top;
        window.Width = width; window.Height = height;
        await UntilAsync(() => Math.Abs(window.ActualWidth - width) < 1 && Math.Abs(window.ActualHeight - height) < 1);
        window.UpdateLayout();
    }

    private static async Task ClickAsync(ButtonBase element)
    {
        element.UpdateLayout();
        Assert.True(element.IsVisible && element.IsEnabled, $"{element.Name} must be available for a native click.");
        var at = element.PointToScreen(new Point(element.ActualWidth / 2, element.ActualHeight / 2));
        Assert.True(SetCursorPos((int)at.X, (int)at.Y));
        try { await UntilAsync(() => element.IsMouseOver); } // Let WPF process the pointer move before sending button input.
        catch (TimeoutException error)
        {
            var window = Window.GetWindow(element);
            var hit = window.InputHitTest(window.PointFromScreen(at)) as DependencyObject;
            var path = new List<string>();
            for (var node = hit; node is not null; node = VisualTreeHelper.GetParent(node))
                path.Add(node is FrameworkElement view ? $"{view.GetType().Name}({view.Name}, {view.ActualWidth}x{view.ActualHeight})" : node.GetType().Name);
            throw new InvalidOperationException($"Pointer at {at}, WPF {Mouse.GetPosition(window)} did not enter {element.Name} ({element.ActualWidth}x{element.ActualHeight}). Hit: {string.Join(" > ", path)}", error);
        }
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
    public Task ThirtyCardsFitAndEverySettingsTabKeepsTheCenteredLiveGrid() => OnUiThreadAsync(async () =>
    {
        var h = Samples(); var main = new MainWindow(h.Engine); main.Show(); Motion.SetEnabled(main, false);
        try
        {
            await UntilAsync(() => main.Model.Tracked.Count == 25);
            Assert.Equal(WindowState.Maximized, main.WindowState);
            Assert.Equal(2, PathGeometry.CreateFromGeometry(((System.Windows.Shapes.Path)main.FindName("MaximizeIcon")).Data).Figures.Count);
            await ResizeAsync(main, 1920, 1040);
            Assert.Single(PathGeometry.CreateFromGeometry(((System.Windows.Shapes.Path)main.FindName("MaximizeIcon")).Data).Figures);
            Assert.Equal(5, main.ColumnCount); Assert.Equal(30, main.Model.Snapshot.LiveCount);
            var scroll = (ScrollViewer)main.FindName("PageScroll");
            var cards = (ItemsControl)main.FindName("WorldCards"); var favorites = (ItemsControl)main.FindName("FavoriteCards");
            Assert.Equal(25, cards.Items.Count); Assert.Equal(5, favorites.Items.Count);
            Assert.Equal(270, cards.ActualHeight); Assert.Equal(54, favorites.ActualHeight);
            Assert.True(scroll.ScrollableHeight < 1); Assert.True(scroll.ExtentHeight < 400);
            Assert.Empty(Children<ListBox>(main)); Assert.Empty(Children<DataGrid>(main));
            Assert.Null(((Grid)main.FindName("TitleBar")).Background);
            WorldTile Tile(int id) => Children<WorldTile>(main).Single(t => t.IsVisible && t.DataContext is WorldCardModel w && w.Id == id);
            var tile = Tile(306); var surface = (WorldCardSurface)tile.FindName("Surface");
            Assert.Equal(new Thickness(0), surface.BorderThickness); Assert.Equal(54, tile.ActualHeight);
            var number = (TextBlock)tile.FindName("WorldNumber"); var ping = (TextBlock)tile.FindName("LatencyValue"); var flag = (FlagGlyph)tile.FindName("RegionFlag");
            var heart = (ToggleButton)tile.FindName("FavoriteToggle"); var star = (System.Windows.Shapes.Path)tile.FindName("MembershipStar");
            Assert.True(heart.TranslatePoint(new Point(), tile).X < star.TranslatePoint(new Point(), tile).X);
            Assert.True(star.TranslatePoint(new Point(), tile).X < number.TranslatePoint(new Point(), tile).X);
            Assert.True(ping.TranslatePoint(new Point(), tile).X < flag.TranslatePoint(new Point(), tile).X);
            Assert.InRange(ping.TranslatePoint(new Point(), tile).X - number.TranslatePoint(new Point(), tile).X - number.ActualWidth, 0, 12);
            Assert.Equal(Color.FromRgb(196, 204, 214), ((SolidColorBrush)star.Fill).Color);
            Assert.Equal(((SolidColorBrush)star.Fill).Color, ((SolidColorBrush)number.Foreground).Color);
            Assert.Equal(Color.FromRgb(216, 181, 117), ((SolidColorBrush)((System.Windows.Shapes.Path)Tile(301).FindName("MembershipStar")).Fill).Color);
            Assert.Equal(Color.FromRgb(255, 150, 144), ((SolidColorBrush)((TextBlock)Tile(310).FindName("WorldNumber")).Foreground).Color);
            Preview(main, "dashboard-1080p.png");
            var tabs = (TabControl)main.FindName("SettingsTabs");
            main.Model.OpenSettings.Execute(null); await UntilAsync(() => !main.IsNavigating);
            Assert.Single(Application.Current.Windows.Cast<MainWindow>());
            Assert.Null(main.FindName("WorldSearch")); Assert.Null(tile.FindName("TrackingToggle")); Assert.Null(tile.FindName("ConnectionBars"));
            var header = (Grid)main.FindName("SharedHeader");
            for (int tab = 0; tab < 3; tab++)
            {
                tabs.SelectedIndex = tab; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); main.UpdateLayout();
                Assert.True(header.IsVisible); Assert.True(cards.IsVisible); Assert.True(favorites.IsVisible);
                Assert.True(cards.TranslatePoint(new Point(), main).Y >= ((Grid)main.FindName("SettingsReveal")).TranslatePoint(new Point(), main).Y + ((Grid)main.FindName("SettingsReveal")).ActualHeight);
                Assert.Equal(30, main.Model.Snapshot.LiveCount);
                if (tab > 0)
                {
                    var sheet = (Border)main.FindName(tab == 1 ? "ApplicationSheet" : "AboutSheet");
                    var at = sheet.TranslatePoint(new Point(), main);
                    Assert.InRange(Math.Abs(at.X + sheet.ActualWidth / 2 - main.ActualWidth / 2), 0, 10);
                }
                Preview(main, $"settings-{tab}-1080p.png");
            }
            tabs.SelectedIndex = 0; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); main.UpdateLayout();
            var showAll = (ToggleButton)main.FindName("ShowAllButton"); int probes = h.Probe.Hosts.Count;
            await ClickAsync(showAll); Assert.True(main.Model.ShowAllWorlds);
            Assert.Equal(probes, h.Probe.Hosts.Count); Assert.Equal(Enumerable.Range(331, 10), main.Model.Hidden.Select(w => w.Id));
            Assert.True(((ItemsControl)main.FindName("HiddenCards")).IsVisible);
            Assert.Equal("Hide all", main.Model.ShowAllLabel); Preview(main, "tracking-expanded.png");
            await ClickAsync(showAll); Assert.False(main.Model.ShowAllWorlds);
            await ClickAsync((ToggleButton)Tile(306).FindName("FavoriteToggle"));
            Assert.False(main.Model.AllWorlds.Single(w => w.Id == 306).IsFavorite); Assert.Equal(5, main.Model.Favorites.Count);
            Assert.Contains("up to five", main.Model.FavoriteFeedback);
            main.Model.ShowSettings = false;
            foreach (var (width, height, scale) in new[] { (1366d, 728d, 1d), (1280d, 693d, 1.5), (960d, 520d, 2d), (683d, 384d, 2d), (480d, 640d, 1d) })
            {
                await ResizeAsync(main, width, height); scroll.ScrollToTop(); main.UpdateLayout();
                Preview(main, $"dashboard-{width}-{scale}.png", scale);
                main.Model.ShowSettings = true;
                for (int tab = 0; tab < 3; tab++)
                {
                    tabs.SelectedIndex = tab; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                    scroll.ScrollToTop(); main.UpdateLayout(); Preview(main, $"settings-{tab}-{width}-{scale}.png", scale);
                }
                main.Model.ShowSettings = false;
            }
        }
        finally { SetCursorPos(1, 1); await CloseAsync(main); await h.DisposeAsync(); }
    });

    [Fact]
    public Task SpritesFadedHiddenWorldsAndFavoriteHeartsWorkWithNativeInput() => OnUiThreadAsync(async () =>
    {
        var h = Samples(); var main = new MainWindow(h.Engine); main.Show(); Motion.SetEnabled(main, false);
        try
        {
            await UntilAsync(() => main.Model.Tracked.Count == 25); await ResizeAsync(main, 1920, 1040);
            main.Model.ShowSettings = true; main.Model.ShowAllWorlds = true;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); main.UpdateLayout();
            WorldTile Tile(int id) => Children<WorldTile>(main).Single(t => t.IsVisible && t.DataContext is WorldCardModel w && w.Id == id);
            foreach (var uri in main.Model.AllWorlds.Select(w => w.ActivitySprite).OfType<string>().Distinct())
            {
                using var stream = Application.GetResourceStream(new Uri(uri)).Stream;
                var frame = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                Assert.InRange(frame.PixelWidth, 1, 32); Assert.InRange(frame.PixelHeight, 1, 32);
            }
            Assert.Equal(23, ((BitmapSource)((System.Windows.Controls.Image)Tile(301).FindName("ActivitySprite")).Source).PixelWidth);
            var inactive = (WorldCardSurface)Tile(340).FindName("Surface"); SetCursorPos(1, 1);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); Assert.InRange(inactive.Opacity, .4, .5);
            var at = inactive.PointToScreen(new Point(inactive.ActualWidth / 2, inactive.ActualHeight / 2));
            SetCursorPos((int)at.X, (int)at.Y); await UntilAsync(() => inactive.IsMouseOver && inactive.Opacity > .8);
            Assert.False(((DropShadowEffect)inactive.Effect).HasAnimatedProperties);
            await ClickAsync((ToggleButton)Tile(301).FindName("FavoriteToggle"));
            await UntilAsync(() => !main.Model.AllWorlds.Single(w => w.Id == 301).IsFavorite);
            await ClickAsync((ToggleButton)Tile(340).FindName("FavoriteToggle"));
            await UntilAsync(() => main.Model.Favorites.Any(w => w.Id == 340));
            Assert.True(main.Model.AllWorlds.Single(w => w.Id == 340).IsTracked); Assert.Equal(5, main.Model.Favorites.Count);
            Assert.Contains(340, h.Store.Settings.FavoriteWorldIds); Assert.DoesNotContain(340, main.Model.Hidden.Select(w => w.Id));
            Preview(main, "tracking-favorites.png");
        }
        catch { Preview(main, "tracking-interaction-failure.png"); throw; }
        finally { SetCursorPos(1, 1); await CloseAsync(main); await h.DisposeAsync(); }
    });

    [Fact]
    public Task SettingsMotionReversesAndProgressNeverMovesTheFooter() => OnUiThreadAsync(async () =>
    {
        var h = Samples(); var main = new MainWindow(h.Engine); main.Show();
        try
        {
            await UntilAsync(() => main.Model.Tracked.Count == 25); await ResizeAsync(main, 1920, 1040);
            bool animate = Motion.CanAnimate(main);
            main.Model.ShowSettings = true;
            if (animate) { Assert.True(main.IsNavigating); Assert.Equal(12, ((Canvas)main.FindName("GearSparks")).Children.Count); }
            main.Model.ShowSettings = false; main.Model.ShowSettings = true; // Reverse an in-flight transition without leaving stale clocks or particles.
            await UntilAsync(() => !main.IsNavigating);
            Assert.True(main.Model.ShowSettings); Assert.Empty(((Canvas)main.FindName("GearSparks")).Children);
            main.Model.SelectedSettingsTab = 1; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            await UntilAsync(() => !main.IsNavigating);
            Assert.True(((Grid)main.FindName("SettingsReveal")).ActualHeight > 250);
            Motion.SetEnabled(main, false); main.Model.ShowSettings = false; main.Model.ShowSettings = true;
            Assert.False(main.IsNavigating); Assert.Empty(((Canvas)main.FindName("GearSparks")).Children);
            var footer = (Grid)main.FindName("ReviewFooter"); main.UpdateLayout(); var before = footer.TranslatePoint(new Point(), main);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            h.Probe.Handler = async (_, token) => { entered.TrySetResult(); await release.Task.WaitAsync(token); return new ProbeResult(ProbeStatus.Success, 10); };
            Task scan = h.Engine.RecheckAsync(); await entered.Task; await UntilAsync(() => main.Model.Snapshot.IsScanning);
            main.UpdateLayout(); Assert.Equal(before, footer.TranslatePoint(new Point(), main)); Assert.Equal(43, footer.ActualHeight);
            Assert.Equal(1, ((ProgressBar)main.FindName("ReviewProgress")).Opacity); Preview(main, "settings-scan-progress.png");
            release.TrySetResult(); await scan; await UntilAsync(() => !main.Model.Snapshot.IsScanning);
            Assert.Equal(before, footer.TranslatePoint(new Point(), main)); Assert.Equal(0, ((ProgressBar)main.FindName("ReviewProgress")).Opacity);
        }
        finally { await CloseAsync(main); await h.DisposeAsync(); }
    });

    [Fact]
    public Task ApplicationValidatesRequiredRangesAndResetKeepsFavoritesAndPlacement() => OnUiThreadAsync(async () =>
    {
        var h = Samples(); var main = new MainWindow(h.Engine); main.Show(); Motion.SetEnabled(main, false);
        try
        {
            await UntilAsync(() => main.Model.Tracked.Count == 25); await ResizeAsync(main, 1920, 1040);
            main.Model.OpenConnectionSettings.Execute(null); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); main.UpdateLayout();
            var apply = (Button)main.FindName("ApplySettingsButton");
            foreach (string invalid in new[] { "", "0", "2001", "abc" }) { main.Model.Cutoff = invalid; Assert.False(main.Model.CanApplySettings); }
            main.Model.Cutoff = "1";
            foreach (string invalid in new[] { "", "4", "601", "abc" }) { main.Model.Interval = invalid; Assert.False(main.Model.CanApplySettings); }
            main.Model.Interval = "5"; Assert.True(main.Model.CanApplySettings);
            main.Model.Cutoff = "2000"; main.Model.Interval = "600"; Assert.True(main.Model.CanApplySettings);
            main.Model.AlwaysOnTop = main.Model.MinimizeToTray = main.Model.CloseToTray = true;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); Assert.True(apply.IsEnabled); await ClickAsync(apply);
            await UntilAsync(() => h.Engine.GetSnapshot().Settings.RefreshIntervalSeconds == 600);
            var placement = new WindowPlacement(50, 60, 1100, 700, true); await h.Engine.SaveWindowPlacementAsync(placement);
            await ClickAsync((Button)main.FindName("ResetDefaultsButton"));
            await UntilAsync(() => h.Engine.GetSnapshot().Settings.RefreshIntervalSeconds == 5);
            Assert.Equal(30, h.Store.Settings.LatencyCutoffMs); Assert.False(h.Store.Settings.AlwaysOnTop); Assert.False(h.Store.Settings.MinimizeToTray); Assert.False(h.Store.Settings.CloseToTray);
            Assert.Equal([301, 302, 303, 304, 305], h.Store.Settings.FavoriteWorldIds); Assert.Equal(placement, h.Store.Settings.Window);
            var input = (TextBox)main.FindName("CutoffInput"); input.Focus(); await h.Engine.CheckTrackedWorldsAsync();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); Assert.Same(input, Keyboard.FocusedElement);
            Assert.True(input.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)));
            main.Model.SelectedSettingsTab = 2; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); main.UpdateLayout();
            Assert.Empty(Children<Expander>(main));
            var profiles = Children<Button>(main).Where(b => b.Tag is string).Select(b => (string)b.Tag).ToArray();
            Assert.Contains("https://github.com/SoraRS", profiles); Assert.Contains("https://discord.com/users/845495758182678548", profiles);
        }
        finally { await CloseAsync(main); await h.DisposeAsync(); }
    });

    [Fact]
    public Task ReadingsKeepCardIdentityOrderScrollAndFocusWhileBackgrounded() => OnUiThreadAsync(async () =>
    {
        var h = Samples(); var main = new MainWindow(h.Engine); var sentinel = new Window { Title = "Foreground sentinel", Width = 350, Height = 180 };
        main.Show();
        try
        {
            await UntilAsync(() => main.Model.Tracked.Count == 25);
            main.WindowState = WindowState.Normal; main.Width = 720; main.Height = 360; main.UpdateLayout();
            var scroll = (ScrollViewer)main.FindName("PageScroll"); scroll.ScrollToVerticalOffset(240); main.UpdateLayout();
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
