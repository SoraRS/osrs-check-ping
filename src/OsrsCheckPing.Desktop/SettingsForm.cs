using OsrsCheckPing.Core;

namespace OsrsCheckPing.Desktop;

internal sealed class SettingsForm : Form
{
    private readonly MonitoringEngine _engine;
    private readonly WorldGrid _worlds = new(editable: true);
    private readonly TextBox _search = new() { Width = 320, PlaceholderText = "Search world, region, or activity", AccessibleName = "Search worlds" };
    private readonly Button _recheck = new() { Text = "Recheck all worlds" };
    private readonly Button _reset = new() { Text = "Reset session toggles" };
    private readonly CheckBox _browseHidden = new()
    {
        Text = "Browse hidden worlds", AutoSize = true, AccessibleName = "Browse hidden worlds",
        Margin = new Padding(10, 10, 0, 0)
    };
    private readonly Label _trackingInfo = Theme.Label("");
    private readonly Label _message = Theme.Label("");
    private readonly NumericUpDown _cutoff = new() { Minimum = 1, Maximum = 2000, Width = 110, AccessibleName = "Latency cutoff in milliseconds" };
    private readonly NumericUpDown _interval = new() { Minimum = 1, Maximum = 60, Width = 110, AccessibleName = "Refresh interval in seconds" };
    private readonly CheckBox _onTop = new() { Text = "Keep the main window always on top", AutoSize = true };
    private readonly CheckBox _minimizeTray = new() { Text = "Minimize to the notification area (tray)", AutoSize = true };
    private readonly CheckBox _closeTray = new() { Text = "Closing the window keeps tracking in the tray", AutoSize = true };
    private readonly Button _apply = new() { Text = "Apply settings" };
    private readonly System.Windows.Forms.Timer _refreshTimer = new() { Interval = 200 };
    private volatile bool _refreshPending = true;
    private string? _shownNotice;

    public SettingsForm(MonitoringEngine engine)
    {
        _engine = engine;
        Text = "Settings · OSRS Check Ping";
        Icon = SystemIcons.Application;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(1110, 700);
        MinimumSize = new Size(900, 540);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;

        var tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(12, 6) };
        var tracking = new TabPage("World tracking") { Padding = new Padding(12) };
        var application = new TabPage("Application") { Padding = new Padding(18), AutoScroll = true };
        tabs.TabPages.Add(tracking);
        tabs.TabPages.Add(application);
        Theme.StyleTabs(tabs);

        var trackingLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
        trackingLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        trackingLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        trackingLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        trackingLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        trackingLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Padding = new Padding(0, 0, 0, 8) };
        toolbar.Controls.Add(_search);
        toolbar.Controls.Add(_recheck);
        toolbar.Controls.Add(_reset);
        toolbar.Controls.Add(_browseHidden);
        trackingLayout.Controls.Add(toolbar, 0, 0);
        _trackingInfo.MaximumSize = new Size(1000, 0);
        _trackingInfo.Font = new Font("Segoe UI Semibold", 11);
        _trackingInfo.ForeColor = Theme.Gold;
        trackingLayout.Controls.Add(_trackingInfo, 0, 1);
        trackingLayout.Controls.Add(_worlds, 0, 2);
        var explanation = Theme.Label("Favorites reserve slots within the 20-world limit. Session toggles reset when you exit.\nHidden worlds rest between five-minute reviews. Browsing them does not start live checks.");
        explanation.MaximumSize = new Size(1000, 0);
        trackingLayout.Controls.Add(explanation, 0, 3);
        tracking.Controls.Add(trackingLayout);

        var preferences = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1 };
        preferences.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var monitoring = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2 };
        monitoring.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        monitoring.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddFullWidth(monitoring, Theme.Label("Connection monitoring", true));
        AddPreference(monitoring, "Acceptable latency (ms)", _cutoff);
        AddPreference(monitoring, "Live update interval (seconds)", _interval);
        var rule = Theme.Label("Up to 20 worlds are live at once. Hidden worlds are reviewed every five minutes.\nSlow or failed checks for a minute remove an unpinned world until a later review.");
        rule.MaximumSize = new Size(850, 0);
        AddFullWidth(monitoring, rule);
        var monitoringCard = new RunePanel { Dock = DockStyle.Top, AutoSize = true, Margin = new Padding(0, 0, 0, 16) };
        monitoringCard.Controls.Add(monitoring);
        preferences.Controls.Add(monitoringCard, 0, 0);

        var window = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1 };
        window.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddFullWidth(window, Theme.Label("Window & tray", true));
        AddFullWidth(window, _onTop);
        AddFullWidth(window, _minimizeTray);
        AddFullWidth(window, _closeTray);
        var trayHelp = Theme.Label("Tracking continues while hidden. Use Exit in the tray menu to quit.\nWindow size and position are remembered automatically.");
        trayHelp.MaximumSize = new Size(850, 0);
        AddFullWidth(window, trayHelp);
        var windowCard = new RunePanel { Dock = DockStyle.Top, AutoSize = true, Margin = new Padding(0, 0, 0, 16) };
        windowCard.Controls.Add(window);
        preferences.Controls.Add(windowCard, 0, 1);
        preferences.Controls.Add(_apply, 0, 2);
        application.Controls.Add(preferences);

        var outer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(8) };
        outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        outer.Controls.Add(tabs, 0, 0);
        _message.MaximumSize = new Size(1050, 0);
        outer.Controls.Add(_message, 0, 1);
        Controls.Add(outer);
        Theme.Apply(this);
        _message.ForeColor = Theme.Gold;

        var settings = engine.GetSnapshot().Settings;
        _cutoff.Value = settings.LatencyCutoffMs;
        _interval.Value = settings.RefreshIntervalSeconds;
        _onTop.Checked = settings.AlwaysOnTop;
        _minimizeTray.Checked = settings.MinimizeToTray;
        _closeTray.Checked = settings.CloseToTray;

        _search.TextChanged += (_, _) => RefreshWorlds();
        _browseHidden.CheckedChanged += (_, _) => RefreshWorlds();
        _worlds.TrackingChanged += (id, enabled) =>
        {
            if (!_engine.SetTracking(id, enabled))
                _message.Text = "All 20 slots are reserved. Disable another manually selected world first.";
            _refreshPending = true;
        };
        _worlds.FavoriteChanged += async (id, enabled) =>
        {
            try
            {
                bool accepted = await _engine.SetFavoriteAsync(id, enabled);
                if (!IsDisposed)
                {
                    _message.Text = accepted ? "Always-tracked worlds updated." : "You can always track five worlds. Uncheck another world first.";
                    _refreshPending = true;
                }
            }
            catch (OperationCanceledException) when (IsDisposed || Disposing) { }
            catch (Exception error) { if (!IsDisposed) _message.Text = error.Message; }
        };
        _recheck.Click += async (_, _) =>
        {
            _recheck.Enabled = false;
            try { await _engine.RecheckAsync(); }
            catch (OperationCanceledException) when (IsDisposed || Disposing) { }
            catch (Exception error) { if (!IsDisposed) _message.Text = error.Message; }
            finally { if (!IsDisposed) RefreshWorlds(); }
        };
        _reset.Click += (_, _) =>
        {
            _engine.ResetManualTracking();
            _message.Text = "Session toggles reset. Always-tracked choices are unchanged.";
            RefreshWorlds();
        };
        _apply.Click += async (_, _) =>
        {
            _apply.Enabled = false;
            try
            {
                await _engine.UpdatePreferencesAsync((int)_cutoff.Value, (int)_interval.Value, _onTop.Checked,
                    _minimizeTray.Checked, _closeTray.Checked);
                if (!IsDisposed)
                {
                    var snapshot = _engine.GetSnapshot();
                    _message.Text = snapshot.Notice ?? "Settings saved. Hidden worlds are reviewed every five minutes.";
                }
            }
            catch (OperationCanceledException) when (IsDisposed || Disposing) { }
            catch (Exception error) { if (!IsDisposed) _message.Text = error.Message; }
            finally { if (!IsDisposed) _apply.Enabled = true; }
        };
        _engine.Changed += EngineChanged;
        _refreshTimer.Tick += (_, _) =>
        {
            if (!_refreshPending) return;
            _refreshPending = false;
            RefreshWorlds();
        };
        _refreshTimer.Start();
        FormClosed += (_, _) =>
        {
            _engine.Changed -= EngineChanged;
            _refreshTimer.Dispose();
        };
        Resize += (_, _) =>
        {
            int width = Math.Max(300, ClientSize.Width - 60);
            _trackingInfo.MaximumSize = new Size(width, 0);
            explanation.MaximumSize = new Size(width, 0);
            _message.MaximumSize = new Size(width, 0);
        };
        RefreshWorlds();
    }

    private static void AddFullWidth(TableLayoutPanel panel, Control control)
    {
        int row = panel.RowCount++;
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        control.Margin = new Padding(3, 9, 3, 9);
        panel.Controls.Add(control, 0, row);
        panel.SetColumnSpan(control, panel.ColumnCount);
    }

    private static void AddPreference(TableLayoutPanel panel, string title, Control control)
    {
        int row = panel.RowCount++;
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.Controls.Add(Theme.Label(title), 0, row);
        control.Margin = new Padding(18, 6, 3, 6);
        panel.Controls.Add(control, 1, row);
    }

    private void EngineChanged(object? sender, EventArgs args) => _refreshPending = true;

    private void RefreshWorlds()
    {
        if (IsDisposed) return;
        var snapshot = _engine.GetSnapshot();
        string query = _search.Text.Trim();
        var visible = _browseHidden.Checked ? snapshot.Worlds : snapshot.VisibleWorlds;
        _search.PlaceholderText = _browseHidden.Checked ? "Search all worlds" : "Search your tracked worlds";
        var matching = visible.Where(w => query.Length == 0 || w.Id.ToString().Contains(query, StringComparison.OrdinalIgnoreCase)
            || (w.World?.Region.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
            || (w.World?.Activity.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)).OrderBy(w => w.Id).ToArray();
        _worlds.ShowWorlds(matching, snapshot.Settings);
        _recheck.Enabled = !snapshot.IsScanning;
        _reset.Enabled = !snapshot.IsScanning;
        _trackingInfo.Text = snapshot.IsScanning
            ? $"{(snapshot.IsRecheckingHidden ? "Reviewing hidden worlds" : "Scouting worlds")} · {snapshot.ScanCompleted} / {snapshot.ScanTotal}"
            : $"{snapshot.LiveCount} / 20 live slots   ·   {snapshot.HiddenCount} hidden   ·   {snapshot.Settings.FavoriteWorldIds.Count} / 5 favorites";
        if (snapshot.Notice is not null)
            _message.Text = _shownNotice = snapshot.Notice;
        else if (_message.Text == _shownNotice)
            _message.Text = "";
    }
}
