using OsrsCheckPing.Core;

namespace OsrsCheckPing.Desktop;

internal sealed class MainForm : Form
{
    private readonly MonitoringEngine _engine;
    private readonly WorldGrid _favorites = new();
    private readonly WorldGrid _tracked = new();
    private readonly Label _summary = Theme.Label("Loading preferences…");
    private readonly Label _notice = Theme.Label("");
    private readonly Label _empty = Theme.Label("Preparing the world catalogue…");
    private readonly Label _favoriteTitle = Theme.Label("Always tracked · 0 / 5", true);
    private readonly Label _trackedTitle = Theme.Label("Nearby worlds", true);
    private readonly ProgressBar _progress = new() { Dock = DockStyle.Fill, Height = 7, Visible = false };
    private readonly Button _settingsButton = new() { Text = "Settings", AccessibleName = "Open settings" };
    private readonly Button _recheckButton = new() { Text = "Recheck worlds", AccessibleName = "Recheck all worlds" };
    private readonly SignalEmblem _emblem = new() { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 16, 0) };
    private readonly MetricCard _bestPing = new("Best connection") { Dock = DockStyle.Fill };
    private readonly MetricCard _liveWorlds = new("Live worlds") { Dock = DockStyle.Fill };
    private readonly MetricCard _nextReview = new("Next world review") { Dock = DockStyle.Fill };
    private readonly RunePanel _favoritesSection;
    private readonly TableLayoutPanel _layout;
    private readonly System.Windows.Forms.Timer _refreshTimer = new() { Interval = 200 };
    private readonly NotifyIcon _tray;
    private SettingsForm? _settingsForm;
    private volatile bool _refreshPending = true;
    private bool _placementRestored;
    private bool _closing;
    private bool _allowClose;
    private bool _forceExit;
    private FormWindowState _lastVisibleState = FormWindowState.Normal;
    private MonitorSnapshot? _lastSnapshot;
    private DateTimeOffset _snapshotReceivedAt;

    public MainForm(MonitoringEngine engine)
    {
        _engine = engine;
        Text = "OSRS Check Ping";
        Icon = SystemIcons.Application;
        AutoScaleMode = AutoScaleMode.Dpi;
        MinimumSize = new Size(860, 580);
        var desktop = Screen.PrimaryScreen!.WorkingArea;
        Size = new Size(Math.Min(1140, desktop.Width - 60), Math.Min(900, desktop.Height - 60));
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;

        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Padding = new Padding(0, 0, 0, 12) };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var title = Theme.Label("OSRS CHECK PING", true);
        title.Font = new Font("Segoe UI Semibold", 21);
        title.Margin = new Padding(0, 2, 0, 1);
        var branding = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true };
        var eyebrow = Theme.Label("THE WORLD SCOUT");
        eyebrow.Font = new Font("Segoe UI", 8, FontStyle.Bold);
        eyebrow.ForeColor = Theme.Gold;
        eyebrow.Margin = Padding.Empty;
        branding.Controls.Add(eyebrow);
        branding.Controls.Add(title);
        var tagline = Theme.Label("Less lag. A better place to play.");
        tagline.Margin = Padding.Empty;
        branding.Controls.Add(tagline);
        var actions = new FlowLayoutPanel { AutoSize = true, Anchor = AnchorStyles.Right, WrapContents = false };
        actions.Controls.Add(_recheckButton);
        actions.Controls.Add(_settingsButton);
        header.Controls.Add(_emblem, 0, 0);
        header.Controls.Add(branding, 1, 0);
        header.Controls.Add(actions, 2, 0);

        var metrics = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Padding = new Padding(0, 0, 0, 14) };
        for (int i = 0; i < 3; i++) metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
        _bestPing.Margin = new Padding(0, 0, 8, 0);
        _liveWorlds.Margin = new Padding(4, 0, 4, 0);
        _nextReview.Margin = new Padding(8, 0, 0, 0);
        metrics.Controls.Add(_bestPing, 0, 0);
        metrics.Controls.Add(_liveWorlds, 1, 0);
        metrics.Controls.Add(_nextReview, 2, 0);

        _layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7, Padding = new Padding(24, 18, 24, 12)
        };
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 108));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 124));
        _layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 12));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
        _layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        _notice.MaximumSize = new Size(950, 0);
        _notice.Padding = new Padding(10);
        _favoritesSection = CreateWorldSection(_favoriteTitle, _favorites);
        _favorites.ColumnHeadersVisible = false;
        _favoritesSection.Margin = new Padding(0, 0, 0, 12);
        _favoritesSection.Visible = false;
        var trackedSection = CreateWorldSection(_trackedTitle, _tracked, _empty);
        _empty.AutoSize = false;
        _empty.Dock = DockStyle.Fill;
        _empty.TextAlign = ContentAlignment.MiddleCenter;
        _empty.Padding = new Padding(20);
        _summary.Anchor = AnchorStyles.Left;
        _summary.Font = new Font("Segoe UI", 9);
        _layout.Controls.Add(header, 0, 0);
        _layout.Controls.Add(metrics, 0, 1);
        _layout.Controls.Add(_notice, 0, 2);
        _layout.Controls.Add(_progress, 0, 3);
        _layout.Controls.Add(_favoritesSection, 0, 4);
        _layout.Controls.Add(trackedSection, 0, 5);
        _layout.Controls.Add(_summary, 0, 6);
        Controls.Add(_layout);
        Theme.Apply(this);
        title.ForeColor = Theme.Gold;
        _favoriteTitle.ForeColor = Theme.Gold;
        _trackedTitle.ForeColor = Theme.Gold;
        _notice.ForeColor = Theme.Slow;

        var trayMenu = new ContextMenuStrip();
        trayMenu.Items.Add("Show OSRS Check Ping", null, (_, _) => RestoreFromTray());
        trayMenu.Items.Add("Settings", null, (_, _) => { RestoreFromTray(); OpenSettings(); });
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add("Exit", null, (_, _) => { _forceExit = true; Close(); });
        _tray = new NotifyIcon { Icon = Icon, Text = "OSRS Check Ping", ContextMenuStrip = trayMenu };
        _tray.DoubleClick += (_, _) => RestoreFromTray();

        _engine.Changed += EngineChanged;
        _refreshTimer.Tick += (_, _) =>
        {
            if (_closing) return;
            if (_refreshPending)
            {
                _refreshPending = false;
                RefreshSnapshot();
            }
            else
                RefreshCountdown();
        };
        _refreshTimer.Start();
        _settingsButton.Click += (_, _) => OpenSettings();
        _recheckButton.Click += async (_, _) => await RecheckAsync();
        KeyDown += (_, args) =>
        {
            if (args.Control && args.KeyCode == Keys.Oemcomma)
            {
                OpenSettings();
                args.SuppressKeyPress = true;
            }
        };
        Shown += async (_, _) =>
        {
            try { await _engine.StartAsync(); }
            catch (OperationCanceledException) when (_closing) { }
            catch (Exception error) { ShowError(error); }
        };
        Resize += (_, _) => HandleResize();
        FormClosing += HandleClosing;
        FormClosed += (_, _) =>
        {
            _engine.Changed -= EngineChanged;
            _refreshTimer.Dispose();
            _tray.Visible = false;
            _tray.Dispose();
            trayMenu.Dispose();
        };
    }

    private void EngineChanged(object? sender, EventArgs args) => _refreshPending = true;

    private static RunePanel CreateWorldSection(Label title, WorldGrid grid, Label? empty = null)
    {
        var section = new RunePanel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        title.Margin = new Padding(4, 0, 4, 12);
        content.Controls.Add(title, 0, 0);
        var body = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        body.Controls.Add(grid);
        if (empty is not null) body.Controls.Add(empty);
        content.Controls.Add(body, 0, 1);
        section.Controls.Add(content);
        return section;
    }

    private void RefreshSnapshot()
    {
        var snapshot = _engine.GetSnapshot();
        _lastSnapshot = snapshot;
        _snapshotReceivedAt = DateTimeOffset.UtcNow;
        if (snapshot.SettingsLoaded && !_placementRestored)
        {
            _placementRestored = true;
            RestorePlacement(snapshot.Settings.Window);
        }
        TopMost = snapshot.Settings.AlwaysOnTop;
        _tray.Visible = snapshot.Settings.MinimizeToTray || snapshot.Settings.CloseToTray;
        _settingsButton.Enabled = snapshot.SettingsLoaded;
        _recheckButton.Enabled = snapshot.SettingsLoaded && !snapshot.IsScanning;
        _favorites.ShowWorlds(snapshot.Favorites, snapshot.Settings);
        _tracked.ShowWorlds(snapshot.Tracked, snapshot.Settings);
        _favoriteTitle.Text = $"★  Always tracked   ·   {snapshot.Favorites.Count} / 5";
        _trackedTitle.Text = $"Fastest worlds   ·   {snapshot.Tracked.Count} connections";
        _favoritesSection.Visible = snapshot.Favorites.Count > 0;
        SizeWorldSections();
        _summary.Text = snapshot.IsScanning
            ? snapshot.IsRecheckingHidden ? $"◈  Reviewing hidden worlds   ·   {snapshot.ScanCompleted} / {snapshot.ScanTotal}"
                : snapshot.ScanTotal == 0 ? "◈  Finding your worlds…" : $"◈  Scouting worlds   ·   {snapshot.ScanCompleted} / {snapshot.ScanTotal}"
            : $"●  Live monitoring   ·   Acceptable latency ≤ {snapshot.Settings.LatencyCutoffMs} ms   ·   " +
                $"{snapshot.HiddenCount} worlds resting until the next review";
        _summary.ForeColor = snapshot.IsScanning ? Theme.Gold : Theme.Muted;
        _emblem.SignalColor = snapshot.IsScanning ? Theme.Gold : snapshot.LiveCount > 0 ? Theme.Good : Theme.Muted;
        var best = snapshot.Worlds.Where(w => w.IsTracked && w.LatestResult?.Status == ProbeStatus.Success)
            .OrderBy(w => w.LatestResult!.RoundTripMilliseconds).ThenBy(w => w.Id).FirstOrDefault();
        _bestPing.SetValue(best is null ? "—" : $"{best.LatestResult!.RoundTripMilliseconds} ms",
            best is null ? "Waiting for a response" : $"World {best.Id} · {best.World!.Region}", Theme.Good);
        _liveWorlds.SetValue($"{snapshot.LiveCount} / {MonitoringEngine.MaximumTrackedWorlds}",
            $"{snapshot.Settings.RefreshIntervalSeconds}s updates · {snapshot.Favorites.Count} always tracked");
        RefreshCountdown();
        _progress.Visible = snapshot.IsScanning;
        _progress.Style = snapshot.ScanTotal == 0 ? ProgressBarStyle.Marquee : ProgressBarStyle.Continuous;
        _progress.Maximum = Math.Max(snapshot.ScanTotal, 1);
        _progress.Value = Math.Min(snapshot.ScanCompleted, _progress.Maximum);
        _notice.Text = snapshot.Notice ?? "";
        _notice.Visible = snapshot.Notice is not null;
        _empty.Visible = snapshot.Tracked.Count == 0;
        _empty.Text = snapshot.IsScanning ? "Scouting for your fastest connections…"
            : snapshot.Worlds.All(w => w.World is null) ? "Your world list is waiting.\nSelect Recheck worlds to try again."
            : "No automatic worlds to show yet.\nAdjust the cutoff in Settings, or wait for the next world review.";
        if (_empty.Visible) _empty.BringToFront();
    }

    private void RefreshCountdown()
    {
        if (_lastSnapshot is not { } snapshot) return;
        if (snapshot.IsScanning)
            _nextReview.SetValue("Scouting…", snapshot.IsRecheckingHidden ? "Updating the top 20" : "Finding the best connections", Theme.Gold);
        else if (snapshot.NextHiddenRecheckAt is { } next)
        {
            var remaining = next - snapshot.CapturedAt - (DateTimeOffset.UtcNow - _snapshotReceivedAt);
            int seconds = Math.Max(0, (int)Math.Ceiling(remaining.TotalSeconds));
            _nextReview.SetValue($"{seconds / 60:00}:{seconds % 60:00}", $"{snapshot.HiddenCount} hidden worlds · every 5 min", Theme.Gold);
        }
        else
            _nextReview.SetValue("—", "After the first world scan", Theme.Gold);
    }

    private void SizeWorldSections()
    {
        float scale = DeviceDpi / 96f;
        bool compact = ClientSize.Height < 700 * scale;
        _layout.RowStyles[0].Height = (compact ? 88 : 108) * scale;
        _layout.RowStyles[1].Height = (compact ? 104 : 124) * scale;
        int count = _lastSnapshot?.Favorites.Count ?? 0;
        int visibleRows = compact ? Math.Min(count, 1) : ClientSize.Height < 800 * scale ? Math.Min(count, 2) : count;
        _layout.RowStyles[4].Height = count == 0 ? 0
            : visibleRows * _favorites.RowTemplate.Height + _favoritesSection.Padding.Vertical
                + _favoriteTitle.PreferredHeight + _favoriteTitle.Margin.Vertical + 16 * scale;
    }

    private async Task RecheckAsync()
    {
        try { await _engine.RecheckAsync(); }
        catch (OperationCanceledException) when (_closing) { }
        catch (Exception error) { ShowError(error); }
    }

    private void OpenSettings()
    {
        if (!_engine.GetSnapshot().SettingsLoaded || _closing) return;
        if (_settingsForm is { IsDisposed: false })
        {
            _settingsForm.Activate();
            return;
        }
        _settingsForm = new SettingsForm(_engine);
        _settingsForm.Show(this);
    }

    private void HandleResize()
    {
        if (WindowState == FormWindowState.Minimized)
        {
            if (_engine.GetSnapshot().Settings.MinimizeToTray && !_closing)
            {
                HideToTray();
            }
        }
        else
            _lastVisibleState = WindowState;
        _notice.MaximumSize = new Size(Math.Max(200, ClientSize.Width - 50), 0);
        SizeWorldSections();
    }

    private void RestoreFromTray()
    {
        Show();
        WindowState = _lastVisibleState;
        Activate();
        if (_settingsForm is { IsDisposed: false })
            _settingsForm.Show(this);
    }

    private void HideToTray()
    {
        if (_settingsForm is { IsDisposed: false })
            _settingsForm.Hide();
        Hide();
    }

    private void RestorePlacement(WindowPlacement? placement)
    {
        if (placement is null) return;
        var requested = new Rectangle(placement.X, placement.Y, Math.Max(MinimumSize.Width, placement.Width),
            Math.Max(MinimumSize.Height, placement.Height));
        var screen = Screen.AllScreens.FirstOrDefault(s => s.WorkingArea.IntersectsWith(requested)) ?? Screen.PrimaryScreen!;
        var area = screen.WorkingArea;
        requested.Width = Math.Min(requested.Width, area.Width);
        requested.Height = Math.Min(requested.Height, area.Height);
        requested.X = Math.Clamp(requested.X, area.Left, Math.Max(area.Left, area.Right - requested.Width));
        requested.Y = Math.Clamp(requested.Y, area.Top, Math.Max(area.Top, area.Bottom - requested.Height));
        StartPosition = FormStartPosition.Manual;
        Bounds = requested;
        if (placement.Maximized)
            WindowState = FormWindowState.Maximized;
    }

    private async void HandleClosing(object? sender, FormClosingEventArgs args)
    {
        if (_allowClose) return;
        args.Cancel = true;
        if (_closing) return;
        if (!_forceExit && args.CloseReason == CloseReason.UserClosing && _engine.GetSnapshot().Settings.CloseToTray)
        {
            HideToTray();
            return;
        }
        _closing = true;
        _refreshTimer.Stop();
        _settingsForm?.Close();
        _settingsButton.Enabled = false;
        try
        {
            if (_engine.GetSnapshot().SettingsLoaded)
            {
                Rectangle bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
                await _engine.SaveWindowPlacementAsync(new WindowPlacement(bounds.X, bounds.Y, bounds.Width, bounds.Height,
                    _lastVisibleState == FormWindowState.Maximized));
            }
        }
        finally
        {
            try { await _engine.DisposeAsync(); }
            finally
            {
                _allowClose = true;
                if (IsHandleCreated && !IsDisposed)
                    BeginInvoke(Close);
            }
        }
    }

    private void ShowError(Exception error)
    {
        if (!_closing)
            MessageBox.Show(this, error.Message, "OSRS Check Ping", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
