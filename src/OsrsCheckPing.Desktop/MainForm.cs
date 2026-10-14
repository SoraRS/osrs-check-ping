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
    private readonly Button _retryButton = new() { Text = "Retry world scan", Visible = false };
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

    public MainForm(MonitoringEngine engine)
    {
        _engine = engine;
        Text = "OSRS Check Ping";
        Icon = SystemIcons.Application;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(1030, 660);
        MinimumSize = new Size(790, 480);
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;

        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(0, 0, 0, 5) };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var title = Theme.Label("OSRS CHECK PING", true);
        title.Font = new Font("Segoe UI", 19, FontStyle.Bold);
        header.Controls.Add(title, 0, 0);
        _settingsButton.Anchor = AnchorStyles.Right;
        header.Controls.Add(_settingsButton, 1, 0);

        _layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 10, Padding = new Padding(18)
        };
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        _layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 12));
        _layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        _layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _notice.MaximumSize = new Size(950, 0);
        _empty.MaximumSize = new Size(950, 0);
        _layout.Controls.Add(header, 0, 0);
        _layout.Controls.Add(_summary, 0, 1);
        _layout.Controls.Add(_notice, 0, 2);
        _layout.Controls.Add(_progress, 0, 3);
        _layout.Controls.Add(_favoriteTitle, 0, 4);
        _layout.Controls.Add(_favorites, 0, 5);
        _layout.Controls.Add(_trackedTitle, 0, 6);
        _layout.Controls.Add(_tracked, 0, 7);
        _layout.Controls.Add(_empty, 0, 8);
        _layout.Controls.Add(_retryButton, 0, 9);
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
            if (!_refreshPending || _closing) return;
            _refreshPending = false;
            RefreshSnapshot();
        };
        _refreshTimer.Start();
        _settingsButton.Click += (_, _) => OpenSettings();
        _retryButton.Click += async (_, _) => await RecheckAsync();
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

    private void RefreshSnapshot()
    {
        var snapshot = _engine.GetSnapshot();
        if (snapshot.SettingsLoaded && !_placementRestored)
        {
            _placementRestored = true;
            RestorePlacement(snapshot.Settings.Window);
        }
        TopMost = snapshot.Settings.AlwaysOnTop;
        _tray.Visible = snapshot.Settings.MinimizeToTray || snapshot.Settings.CloseToTray;
        _settingsButton.Enabled = snapshot.SettingsLoaded;
        _favorites.ShowWorlds(snapshot.Favorites, snapshot.Settings);
        _tracked.ShowWorlds(snapshot.Tracked, snapshot.Settings);
        _favoriteTitle.Text = $"Always tracked · {snapshot.Favorites.Count} / 5";
        _trackedTitle.Text = $"Other tracked worlds · {snapshot.Tracked.Count}";
        _layout.RowStyles[5].Height = (snapshot.Favorites.Count == 0 ? 1 : snapshot.Favorites.Count) * _favorites.RowTemplate.Height
            + _favorites.ColumnHeadersHeight + 5;
        _summary.Text = snapshot.IsScanning
            ? snapshot.ScanTotal == 0 ? "Downloading the world catalogue…" : $"Checking all worlds · {snapshot.ScanCompleted} / {snapshot.ScanTotal}"
            : $"Cutoff ≤ {snapshot.Settings.LatencyCutoffMs} ms   ·   Updates every {snapshot.Settings.RefreshIntervalSeconds} s   ·   " +
                $"{snapshot.Worlds.Count(w => w.World is not null)} worlds discovered";
        _progress.Visible = snapshot.IsScanning;
        _progress.Style = snapshot.ScanTotal == 0 ? ProgressBarStyle.Marquee : ProgressBarStyle.Continuous;
        _progress.Maximum = Math.Max(snapshot.ScanTotal, 1);
        _progress.Value = Math.Min(snapshot.ScanCompleted, _progress.Maximum);
        _notice.Text = snapshot.Notice ?? "";
        _notice.Visible = snapshot.Notice is not null;
        _empty.Visible = snapshot.Tracked.Count == 0;
        _empty.Text = snapshot.IsScanning ? "Checking every world once. Live tracking resumes when the scan finishes."
            : snapshot.Worlds.All(w => w.World is null) ? "No catalogue is available. Open settings or retry the world scan."
            : "No other worlds are being tracked. Open World tracking to enable worlds or change the cutoff and recheck.";
        _retryButton.Visible = !snapshot.IsScanning && snapshot.Worlds.All(w => w.World is null);
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
        _empty.MaximumSize = _notice.MaximumSize;
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
