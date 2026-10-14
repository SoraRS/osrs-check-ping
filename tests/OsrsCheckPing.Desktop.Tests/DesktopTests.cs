using OsrsCheckPing.Core;
using OsrsCheckPing.Core.Tests;
using OsrsCheckPing.Desktop;

namespace OsrsCheckPing.Desktop.Tests;

public sealed class DesktopTests
{
    [Fact]
    public async Task NativeIcmpReachesWindowsLoopbackWithoutElevation()
    {
        var result = await new IcmpLatencyProbe().ProbeAsync("127.0.0.1", CancellationToken.None);
        Assert.Equal(ProbeStatus.Success, result.Status);
        Assert.True(result.RoundTripMilliseconds >= 0);
    }

    [Fact]
    public Task MainWindowLoadsWorldsAndSettingsControlsUpdateSharedEngine() => OnUiThreadAsync(async () =>
    {
        var h = new Harness();
        using var main = new MainForm(h.Engine);
        main.Show();
        try
        {
            await UntilAsync(() => h.Engine.GetSnapshot().Worlds.Count == 4 &&
                Controls<WorldGrid>(main).Sum(grid => grid.Rows.Count) == 4);
            Assert.Equal("OSRS Check Ping", main.Text);
            Controls<Button>(main).Single(button => button.Text == "Settings").PerformClick();
            var settings = Assert.Single(main.OwnedForms.OfType<SettingsForm>());
            var worlds = Assert.Single(Controls<WorldGrid>(settings));
            Assert.Equal(4, worlds.Rows.Count);
            worlds.Rows[0].Cells["favorite"].Value = true;
            await UntilAsync(() => h.Engine.GetSnapshot().Favorites.Count == 1 && worlds.Rows[0].Cells["track"].ReadOnly);
            Assert.True(worlds.Rows[0].Cells["track"].Value is true);

            var tabs = Assert.Single(Controls<TabControl>(settings));
            tabs.SelectedIndex = 1;
            Controls<NumericUpDown>(settings).Single(control => control.AccessibleName == "Latency cutoff in milliseconds").Value = 80;
            Controls<CheckBox>(settings).Single(control => control.Text == "Keep the main window always on top").Checked = true;
            Controls<Button>(settings).Single(button => button.Text == "Apply settings").PerformClick();
            await UntilAsync(() => h.Engine.GetSnapshot().Settings.LatencyCutoffMs == 80 && main.TopMost);
            Assert.Contains(301, h.Store.Settings.FavoriteWorldIds);
        }
        finally
        {
            await CloseAsync(main);
            await h.DisposeAsync();
        }
    });

    [Fact]
    public Task GridPreservesSelectedAndFirstVisibleWorldWhenRowsReorder() => OnUiThreadAsync(() =>
    {
        using var form = new Form { ClientSize = new Size(1100, 200) };
        using var grid = new WorldGrid();
        form.Controls.Add(grid);
        form.Show();
        var worlds = Enumerable.Range(301, 12).Select(id => new WorldSnapshot(id,
            new WorldInfo(id, 1, $"world-{id}.runescape.com", "-", 0, 100), false, true, null, true, false,
            new ProbeResult(ProbeStatus.Success, 30), DateTimeOffset.UtcNow, false, 0)).ToArray();
        grid.ShowWorlds(worlds, new AppSettings());
        grid.CurrentCell = grid.Rows[3].Cells[0];
        grid.FirstDisplayedScrollingRowIndex = 2;
        grid.ShowWorlds(worlds.Skip(10).Concat(worlds.Take(10)).ToArray(), new AppSettings());
        Assert.Equal(304, ((WorldSnapshot)grid.CurrentRow!.Tag!).Id);
        Assert.Equal(303, ((WorldSnapshot)grid.Rows[grid.FirstDisplayedScrollingRowIndex].Tag!).Id);
        form.Close();
        return Task.CompletedTask;
    });

    [Fact]
    public Task SavedOffscreenPlacementIsRestoredInsideCurrentDesktop() => OnUiThreadAsync(async () =>
    {
        var h = new Harness();
        h.Store.Settings = new AppSettings { Window = new WindowPlacement(-50000, -50000, 1000, 600, false) };
        using var main = new MainForm(h.Engine);
        main.Show();
        try
        {
            await UntilAsync(() => main.StartPosition == FormStartPosition.Manual);
            Assert.Contains(Screen.AllScreens, screen => screen.WorkingArea.Contains(main.Bounds));
        }
        finally
        {
            await CloseAsync(main);
            await h.DisposeAsync();
        }
        Assert.NotNull(h.Store.Settings.Window);
        Assert.True(h.Store.Settings.Window.X > -50000);
    });

    private static IEnumerable<T> Controls<T>(Control parent) where T : Control => parent.Controls.Cast<Control>()
        .SelectMany(child => (child is T match ? new[] { match } : []).Concat(Controls<T>(child)));

    private static async Task UntilAsync(Func<bool> condition)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!condition()) await Task.Delay(25, deadline.Token);
    }

    private static async Task CloseAsync(Form form)
    {
        if (form.IsDisposed) return;
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        form.FormClosed += (_, _) => closed.TrySetResult();
        form.Close();
        await closed.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    private static async Task OnUiThreadAsync(Func<Task> test)
    {
        var complete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            using var context = new ApplicationContext();
            using var dispatcher = new Control();
            _ = dispatcher.Handle;
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            dispatcher.BeginInvoke(async () =>
            {
                try { await test(); complete.TrySetResult(); }
                catch (Exception error) { complete.TrySetException(error); }
                finally { context.ExitThread(); }
            });
            Application.Run(context);
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await complete.Task.WaitAsync(TimeSpan.FromSeconds(40));
    }
}
