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
        h.Store.Settings = new AppSettings { LatencyCutoffMs = 60, FavoriteWorldIds = [301, 302, 303, 304, 305] };
        string[] activities = ["2200 skill total", "Wintertodt", "Tempoross", "Forestry", "Guardians of the Rift", "Trading", "Agility Training", "Blast Furnace", "Fishing Trawler", "PvP", "A long activity description that must remain readable", "Nex FFA", "Nightmare of Ashihama", "Mastering Mixology", "Zalcano", "Volcanic Mine", "Tombs of Amascut", "House Party, Gilded Altar", "Sorceress's Garden", "Salvaging"];
        h.Catalogue.Worlds = Enumerable.Range(301, 40).Select(id => new WorldInfo(id,
            id == 301 ? (uint)(WorldFlags.Members | WorldFlags.SkillTotal) : id == 310 ? (uint)(WorldFlags.Members | WorldFlags.Pvp) : (uint)(id % 2),
            $"world-{id}.runescape.com", id == 337 ? "-" : activities[(id - 301) % activities.Length],
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
    public Task ThirtyFiveCardsFitInSeparateNumericColumnsAndSettingsKeepTheGrid() => OnUiThreadAsync(async () =>
    {
        var h = Samples(); var main = new MainWindow(h.Engine); main.Show(); Motion.SetEnabled(main, false);
        try
        {
            await UntilAsync(() => main.Model.Tracked.Count == 30);
            Assert.Equal(WindowState.Maximized, main.WindowState);
            Assert.Equal(2, PathGeometry.CreateFromGeometry(((System.Windows.Shapes.Path)main.FindName("MaximizeIcon")).Data).Figures.Count);
            await ResizeAsync(main, 1920, 1040);
            Assert.Single(PathGeometry.CreateFromGeometry(((System.Windows.Shapes.Path)main.FindName("MaximizeIcon")).Data).Figures);
            Assert.Equal(3, main.ColumnCount); Assert.Equal(35, main.Model.Snapshot.LiveCount);
            var scroll = (ScrollViewer)main.FindName("PageScroll");
            var cards = (ItemsControl)main.FindName("MemberCards"); var favorites = (ItemsControl)main.FindName("FavoriteCards");
            Assert.Equal(15, cards.Items.Count); Assert.Equal(15, ((ItemsControl)main.FindName("FreeCards")).Items.Count); Assert.Equal(5, favorites.Items.Count);
            Assert.Equal(220, cards.ActualHeight); Assert.Equal(44, favorites.ActualHeight);
            Assert.True(scroll.ScrollableHeight < 1); Assert.True(scroll.ExtentHeight < 800);
            Assert.Empty(Children<ListBox>(main)); Assert.Empty(Children<DataGrid>(main));
            Assert.Null(((Grid)main.FindName("TitleBar")).Background);
            WorldTile Tile(int id) => Children<WorldTile>(main).Single(t => t.IsVisible && t.DataContext is WorldCardModel w && w.Id == id);
            var tile = Tile(306); var surface = (WorldCardSurface)tile.FindName("Surface");
            Assert.Equal(new Thickness(0), surface.BorderThickness); Assert.Equal(44, tile.ActualHeight);
            var number = (TextBlock)tile.FindName("WorldNumber"); var ping = (TextBlock)tile.FindName("LatencyValue"); var flag = (FlagGlyph)tile.FindName("RegionFlag");
            var heart = (ToggleButton)tile.FindName("FavoriteToggle");
            Assert.Null(tile.FindName("MembershipStar")); Assert.False(flag.IsVisible);
            Assert.True(heart.TranslatePoint(new Point(), tile).X < number.TranslatePoint(new Point(), tile).X);
            Assert.InRange(ping.TranslatePoint(new Point(), tile).X - number.TranslatePoint(new Point(), tile).X - number.ActualWidth, 0, 12);
            Assert.Equal(Color.FromRgb(157, 165, 175), ((SolidColorBrush)number.Foreground).Color);
            Assert.Equal(Color.FromRgb(224, 188, 85), ((SolidColorBrush)((TextBlock)Tile(301).FindName("WorldNumber")).Foreground).Color);
            Assert.Equal(Color.FromRgb(255, 150, 144), ((SolidColorBrush)((TextBlock)Tile(310).FindName("WorldNumber")).Foreground).Color);
            Assert.Equal(301, main.Model.BestWorld!.Id);
            foreach (var panelName in new[] { "MemberCards", "FreeCards" })
            {
                var section = (ItemsControl)main.FindName(panelName);
                var tiles = Children<WorldTile>(section).OrderBy(t => ((WorldCardModel)t.DataContext).Id).ToArray();
                for (int i = 0; i < tiles.Length; i++)
                {
                    var (row, column) = WorldColumnLayout.Position(i, tiles.Length, 3);
                    var point = tiles[i].TranslatePoint(new Point(), section);
                    Assert.InRange(Math.Abs(point.Y - row * 44), 0, 1);
                    Assert.InRange(Math.Abs(point.X - column * section.ActualWidth / 3), 0, 1);
                }
            }
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
                var selectedLabel = Children<TextBlock>((TabItem)tabs.Items[tab]).Single();
                Assert.Equal(Color.FromRgb(224, 188, 85), ((SolidColorBrush)selectedLabel.Foreground).Color);
                Assert.True(cards.TranslatePoint(new Point(), main).Y >= ((Grid)main.FindName("SettingsReveal")).TranslatePoint(new Point(), main).Y + ((Grid)main.FindName("SettingsReveal")).ActualHeight);
                Assert.Equal(35, main.Model.Snapshot.LiveCount);
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
            Assert.Equal(probes, h.Probe.Hosts.Count); Assert.Equal(Enumerable.Range(336, 5), main.Model.Hidden.Select(w => w.Id));
            Assert.True(((ItemsControl)main.FindName("HiddenCards")).IsVisible);
            Assert.Equal(ScrollBarVisibility.Hidden, scroll.VerticalScrollBarVisibility);
            Assert.DoesNotContain(Children<TextBlock>((ItemsControl)main.FindName("HiddenCards")), t => t.Text == "Scan result");
            Assert.True(((FlagGlyph)Tile(337).FindName("RegionFlag")).IsVisible);
            Assert.Equal("—", ((TextBlock)Tile(337).FindName("ActivityText")).Text);
            Assert.Contains("Scan result", main.Model.Hidden.Single(w => w.Id == 337).ReadingDetails);
            Assert.Equal("Hide all", main.Model.ShowAllLabel); Preview(main, "tracking-expanded.png");
            await ClickAsync(showAll); Assert.False(main.Model.ShowAllWorlds);
            var rejectedHeart = (ToggleButton)Tile(306).FindName("FavoriteToggle");
            await ClickAsync(rejectedHeart); Assert.False(rejectedHeart.IsChecked);
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
            await UntilAsync(() => main.Model.Tracked.Count == 30); await ResizeAsync(main, 1920, 1040);
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
        bool previousAnimations = SystemParameters.ClientAreaAnimation;
        var h = Samples(); var main = new MainWindow(h.Engine); main.Show();
        try
        {
            // Exercise the animated path even on runners configured for reduced motion. Restore the OS preference in finally.
            Assert.True(SystemParametersInfo(0x1043, 0, new IntPtr(1), 2));
            bool nativeAnimations = false;
            Assert.True(SystemParametersInfoBoolean(0x1042, 0, ref nativeAnimations, 0));
            Assert.True(nativeAnimations, "Windows must report client-area animation enabled before WPF can exercise it.");
            SendMessageTimeout(new IntPtr(0xffff), 0x001A, new IntPtr(0x1043), IntPtr.Zero, 2, 1000, out _);
            await UntilAsync(() => SystemParameters.ClientAreaAnimation);
            await UntilAsync(() => main.Model.Tracked.Count == 30); await ResizeAsync(main, 1920, 1040);
            bool animate = Motion.CanAnimate(main); Assert.True(animate);
            double originalY = ((ItemsControl)main.FindName("MemberCards")).TranslatePoint(new Point(), main).Y;
            main.Model.ShowSettings = true;
            if (animate)
            {
                Assert.True(main.IsNavigating); var sparks = (Canvas)main.FindName("GearSparks"); Assert.Equal(12, sparks.Children.Count);
                double initialX = ((TranslateTransform)((FrameworkElement)sparks.Children[0]).RenderTransform).X;
                await Task.Delay(100);
                Assert.True(Math.Abs(initialX - ((TranslateTransform)((FrameworkElement)sparks.Children[0]).RenderTransform).X) > 1);
                Assert.InRange(((FrameworkElement)sparks.Children[0]).Opacity, 0, .899);
                Assert.Equal(originalY, ((ItemsControl)main.FindName("MemberCards")).TranslatePoint(new Point(), main).Y);
                Assert.All(sparks.Children.Cast<FrameworkElement>(), spark => Assert.Equal(52, Canvas.GetTop(spark)));
                Preview(main, "navigation-sparks.png");
            }
            main.Model.ShowSettings = false;
            await Task.Delay(100);
            Assert.All(((Canvas)main.FindName("GearSparks")).Children.Cast<FrameworkElement>(), spark => Assert.True(((TranslateTransform)spark.RenderTransform).X > 0));
            main.Model.ShowSettings = true; // Reverse an in-flight transition without leaving stale clocks or particles.
            await UntilAsync(() => !main.IsNavigating);
            Assert.True(main.Model.ShowSettings); Assert.Empty(((Canvas)main.FindName("GearSparks")).Children);
            main.Model.SelectedSettingsTab = 1; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            await UntilAsync(() => !main.IsNavigating);
            Assert.True(((Grid)main.FindName("SettingsReveal")).ActualHeight > 250);
            main.Model.ShowSettings = false; main.Model.ShowSettings = true;
            Motion.SetEnabled(main, false); // Also stop a transition already in flight.
            main.Model.ShowSettings = false; main.Model.ShowSettings = true;
            Assert.False(main.IsNavigating); Assert.Empty(((Canvas)main.FindName("GearSparks")).Children);
            var footer = (Grid)main.FindName("ReviewFooter"); main.UpdateLayout(); var before = footer.TranslatePoint(new Point(), main);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            h.Probe.Handler = async (host, token) =>
            {
                if (host == "world-301.runescape.com") { entered.TrySetResult(); await release.Task.WaitAsync(token); }
                return new ProbeResult(ProbeStatus.Success, 10);
            };
            Task scan = h.Engine.RecheckAsync(); await entered.Task; await UntilAsync(() => main.Model.Snapshot.IsScanning && main.Model.Snapshot.ScanCompleted > 0);
            main.UpdateLayout(); Assert.Equal(before, footer.TranslatePoint(new Point(), main)); Assert.Equal(43, footer.ActualHeight);
            var progress = (ProgressBar)main.FindName("ReviewProgress");
            Assert.Equal(1, progress.Opacity); Assert.InRange(progress.Value, 1, 39);
            Assert.All(Children<Border>(progress), border => Assert.Equal(new Thickness(0), border.BorderThickness));
            Preview(main, "settings-scan-progress.png");
            release.TrySetResult(); await scan; await UntilAsync(() => !main.Model.Snapshot.IsScanning);
            Assert.Equal(before, footer.TranslatePoint(new Point(), main)); Assert.Equal(0, ((ProgressBar)main.FindName("ReviewProgress")).Opacity);
        }
        finally
        {
            await CloseAsync(main); await h.DisposeAsync();
            Assert.True(SystemParametersInfo(0x1043, 0, new IntPtr(previousAnimations ? 1 : 0), 2));
            SendMessageTimeout(new IntPtr(0xffff), 0x001A, new IntPtr(0x1043), IntPtr.Zero, 2, 1000, out _);
            await UntilAsync(() => SystemParameters.ClientAreaAnimation == previousAnimations);
        }
    });

    [Fact]
    public Task ApplicationValidatesRequiredRangesAndResetKeepsFavoritesAndPlacement() => OnUiThreadAsync(async () =>
    {
        var h = Samples(); var main = new MainWindow(h.Engine); main.Show(); Motion.SetEnabled(main, false);
        try
        {
            await UntilAsync(() => main.Model.Tracked.Count == 30); await ResizeAsync(main, 1920, 1040);
            main.Model.OpenConnectionSettings.Execute(null); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); main.UpdateLayout();
            var apply = (Button)main.FindName("ApplySettingsButton");
            foreach (string invalid in new[] { "", "0", "2001", "abc" }) { main.Model.Cutoff = invalid; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); Assert.False(main.Model.CanApplySettings); Assert.False(apply.IsEnabled); Assert.True(main.Model.HasCutoffError); }
            main.Model.Cutoff = "1";
            foreach (string invalid in new[] { "", "4", "601", "abc" }) { main.Model.Interval = invalid; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); Assert.False(main.Model.CanApplySettings); Assert.False(apply.IsEnabled); Assert.True(main.Model.HasIntervalError); }
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
            await UntilAsync(() => main.Model.Tracked.Count == 30);
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
            Assert.Equal("No reply", main.Model.Favorites.Single(w => w.Id == 301).Latency);
            Assert.Equal("", main.Model.AllWorlds.Single(w => w.Id == 302).CountryCode);
            Assert.False(main.Model.AllWorlds.Single(w => w.Id == 303).IsDangerous);
            Assert.False(main.Model.ConnectionHealthy); Assert.Contains("2 of 3", main.Model.ConnectionSummary);
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
    [Fact]
    public Task FeedbackExpiresAndTabChangesCancelThePreviousMessage() => OnUiThreadAsync(async () =>
    {
        await using var h = Samples(); await h.Engine.StartAsync();
        var clock = new ManualClock(); using var model = new DashboardViewModel(h.Engine, Dispatcher.CurrentDispatcher, clock);
        _ = await clock.NextDelayAsync(); model.SelectedSettingsTab = 1;
        model.ResetDefaults.Execute(null); await UntilAsync(() => model.HasFeedback);
        var first = await clock.NextDelayAsync(); Assert.Equal(TimeSpan.FromSeconds(5), first.Delay);
        clock.Advance(first); await UntilAsync(() => !model.HasFeedback);
        model.ResetDefaults.Execute(null); await UntilAsync(() => model.HasFeedback);
        var second = await clock.NextDelayAsync();
        model.SelectedSettingsTab = 2; Assert.False(model.HasFeedback);
        model.ResetDefaults.Execute(null); await UntilAsync(() => model.HasFeedback);
        var third = await clock.NextDelayAsync(); clock.Advance(second);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); Assert.True(model.HasFeedback);
        clock.Advance(third); await UntilAsync(() => !model.HasFeedback);
        h.Probe.Return(new ProbeResult(ProbeStatus.TimedOut, Diagnostic: new("ICMP", "127.0.0.1", "TimedOut")));
        await h.Engine.RecheckAsync(); model.Refresh();
        Assert.Contains("no ICMP reply", model.DiagnosticSummary); Assert.Contains("TimedOut", model.ConnectionDiagnostics);
        Assert.DoesNotContain("offline", model.ConnectionSummary, StringComparison.OrdinalIgnoreCase);
    });

    [Fact]
    public Task NativeFrameKeepsTheTaskbarClearAndCapturesMinimizeRestore() => OnUiThreadAsync(async () =>
    {
        var h = Samples(); var main = new MainWindow(h.Engine);
        var animation = new AnimationInfo { Size = (uint)Marshal.SizeOf<AnimationInfo>() };
        Assert.True(SystemParametersAnimation(0x0048, animation.Size, ref animation, 0));
        var previous = animation;
        try
        {
            animation.MinimizeAnimate = 1; Assert.True(SystemParametersAnimation(0x0049, animation.Size, ref animation, 2));
            main.Show(); await UntilAsync(() => main.Model.Tracked.Count == 30);
            var handle = new WindowInteropHelper(main).Handle;
            Assert.Equal(0x00c00000, GetWindowLongPtr(handle, -16).ToInt64() & 0x00c00000);
            Assert.Equal(0, DwmGetWindowAttribute(handle, 9, out var frame, Marshal.SizeOf<FrameRect>()));
            var dpi = VisualTreeHelper.GetDpi(main); var work = SystemParameters.WorkArea;
            Assert.True(frame.Bottom <= (work.Bottom * dpi.DpiScaleY) + 1, $"Visible frame bottom {frame.Bottom} must not cover taskbar at {work.Bottom * dpi.DpiScaleY}.");
            DesktopPreview("desktop-taskbar-maximized.png");
            SystemCommands.MinimizeWindow(main);
            for (int i = 0; i < 8; i++) { await Task.Delay(40); DesktopPreview($"minimize-{i:00}.png"); }
            await UntilAsync(() => main.WindowState == WindowState.Minimized);
            SystemCommands.RestoreWindow(main);
            for (int i = 0; i < 8; i++) { await Task.Delay(40); DesktopPreview($"restore-{i:00}.png"); }
            await UntilAsync(() => main.WindowState == WindowState.Maximized);
        }
        finally
        {
            await CloseAsync(main); await h.DisposeAsync();
            Assert.True(SystemParametersAnimation(0x0049, previous.Size, ref previous, 2));
        }
    });
    private static void DesktopPreview(string name)
    {
        string directory = System.IO.Path.Combine(AppContext.BaseDirectory, "ui-preview"); System.IO.Directory.CreateDirectory(directory);
        int width = (int)SystemParameters.PrimaryScreenWidth, height = (int)SystemParameters.PrimaryScreenHeight;
        using var bitmap = new System.Drawing.Bitmap(width, height);
        using (var graphics = System.Drawing.Graphics.FromImage(bitmap)) graphics.CopyFromScreen(0, 0, 0, 0, bitmap.Size);
        bitmap.Save(System.IO.Path.Combine(directory, name), System.Drawing.Imaging.ImageFormat.Png);
    }
    [StructLayout(LayoutKind.Sequential)] private struct AnimationInfo { public uint Size; public int MinimizeAnimate; }
    [StructLayout(LayoutKind.Sequential)] private struct FrameRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SystemParametersAnimation(uint action, uint parameter, ref AnimationInfo value, uint flags);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out FrameRect value, int size);

    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public InputUnion Data; }
    [StructLayout(LayoutKind.Explicit)] private struct InputUnion { [FieldOffset(0)] public MouseInput Mouse; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseInput { public int X, Y; public uint Data, Flags, Time; public UIntPtr Extra; }
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] input, int size);
    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SystemParametersInfo(uint action, uint parameter, IntPtr value, uint flags);
    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SystemParametersInfoBoolean(uint action, uint parameter, [MarshalAs(UnmanagedType.Bool)] ref bool value, uint flags);
    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW")] private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out UIntPtr result);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
}
