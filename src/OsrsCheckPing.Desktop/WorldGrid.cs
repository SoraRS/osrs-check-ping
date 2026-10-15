using OsrsCheckPing.Core;

namespace OsrsCheckPing.Desktop;

internal sealed class WorldGrid : DataGridView
{
    private readonly bool _editable;
    private bool _updating;
    public event Action<int, bool>? TrackingChanged;
    public event Action<int, bool>? FavoriteChanged;

    public WorldGrid(bool editable = false)
    {
        _editable = editable;
        DoubleBuffered = true;
        Dock = DockStyle.Fill;
        BackgroundColor = Theme.Panel;
        BorderStyle = BorderStyle.None;
        GridColor = Theme.Panel;
        CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        AutoGenerateColumns = false;
        AllowUserToAddRows = false;
        AllowUserToDeleteRows = false;
        AllowUserToResizeRows = false;
        RowHeadersVisible = false;
        MultiSelect = false;
        SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        EnableHeadersVisualStyles = false;
        ColumnHeadersHeight = 38;
        ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        RowTemplate.Height = 34;
        DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Theme.Panel, ForeColor = Theme.Text,
            SelectionBackColor = Color.FromArgb(67, 62, 45), SelectionForeColor = Theme.Text,
            Padding = new Padding(8, 0, 8, 0)
        };
        AlternatingRowsDefaultCellStyle.BackColor = Theme.Alternate;
        ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Theme.Panel, ForeColor = Theme.Gold,
            SelectionBackColor = Theme.Panel, Padding = new Padding(5)
        };
        if (editable)
        {
            AddCheckColumn("track", "Track");
            AddCheckColumn("favorite", "Always");
        }
        AddTextColumn("world", "World", 55, 55);
        AddTextColumn("region", "Region", 105, 100);
        AddTextColumn("access", "Access", 70, 66);
        AddTextColumn("activity", "Activity", 145, 90);
        AddTextColumn("ping", "Latency", 110, 104);
        AddTextColumn("status", "Connection", 150, 100);
        if (editable)
        {
            AddTextColumn("source", "Reading", 95, 90);
            AddTextColumn("checked", "Checked", 75, 75);
        }
        CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (IsCurrentCellDirty && CurrentCell is DataGridViewCheckBoxCell)
                CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        CellValueChanged += HandleValueChanged;
        CellPainting += PaintLatency;
    }

    private void PaintLatency(object? sender, DataGridViewCellPaintingEventArgs args)
    {
        if (args.RowIndex < 0 || args.ColumnIndex < 0 || Columns[args.ColumnIndex].Name != "ping" ||
            Rows[args.RowIndex].Tag is not WorldSnapshot world || args.Graphics is null || args.CellStyle is null)
            return;
        args.PaintBackground(args.ClipBounds, true);
        Color color = Rows[args.RowIndex].Cells[args.ColumnIndex].Style.ForeColor;
        bool success = world.LatestResult?.Status == ProbeStatus.Success;
        double latency = world.LatestResult?.RoundTripMilliseconds ?? 0;
        int strength = success ? Math.Clamp(5 - (int)(latency / Math.Max(1, _cutoffMs) * 2), 1, 5) : 0;
        float scale = DeviceDpi / 96f;
        int barWidth = Math.Max(2, (int)(3 * scale));
        int gap = Math.Max(1, (int)(2 * scale));
        int right = args.CellBounds.Right - (int)(9 * scale);
        int bottom = args.CellBounds.Top + args.CellBounds.Height / 2 + (int)(7 * scale);
        for (int i = 0; i < 5; i++)
        {
            int height = (int)((5 + i * 2) * scale);
            using var brush = new SolidBrush(i < strength ? color : Theme.Border);
            args.Graphics.FillRectangle(brush, right - (5 - i) * (barWidth + gap), bottom - height, barWidth, height);
        }
        var textBounds = args.CellBounds;
        textBounds.X += (int)(8 * scale);
        textBounds.Width -= (int)(43 * scale);
        TextRenderer.DrawText(args.Graphics, args.FormattedValue?.ToString() ?? "—", args.CellStyle.Font,
            textBounds, color, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        args.Paint(args.ClipBounds, DataGridViewPaintParts.Border | DataGridViewPaintParts.Focus);
        args.Handled = true;
    }

    private int _cutoffMs = 60;

    private void AddCheckColumn(string name, string title) => Columns.Add(new DataGridViewCheckBoxColumn
    {
        Name = name, HeaderText = title, Width = 55, MinimumWidth = 55,
        AutoSizeMode = DataGridViewAutoSizeColumnMode.None, SortMode = DataGridViewColumnSortMode.NotSortable
    });

    private void AddTextColumn(string name, string title, int weight, int minimum) => Columns.Add(new DataGridViewTextBoxColumn
    {
        Name = name, HeaderText = title, FillWeight = weight, MinimumWidth = minimum,
        ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable
    });

    private void HandleValueChanged(object? sender, DataGridViewCellEventArgs args)
    {
        if (_updating || args.RowIndex < 0 || args.ColumnIndex < 0 || Rows[args.RowIndex].Tag is not WorldSnapshot world)
            return;
        bool enabled = Rows[args.RowIndex].Cells[args.ColumnIndex].Value is true;
        switch (Columns[args.ColumnIndex].Name)
        {
            case "track": TrackingChanged?.Invoke(world.Id, enabled); break;
            case "favorite": FavoriteChanged?.Invoke(world.Id, enabled); break;
        }
    }

    public void ShowWorlds(IReadOnlyList<WorldSnapshot> worlds, AppSettings settings)
    {
        _cutoffMs = settings.LatencyCutoffMs;
        int? selectedId = CurrentRow?.Tag is WorldSnapshot selected ? selected.Id : null;
        int? firstId = FirstDisplayedScrollingRowIndex >= 0 && Rows[FirstDisplayedScrollingRowIndex].Tag is WorldSnapshot first
            ? first.Id : null;
        int column = CurrentCell?.ColumnIndex ?? 0;
        int horizontalOffset = HorizontalScrollingOffset;
        bool rebuild = Rows.Count != worlds.Count || worlds.Where((w, i) =>
            Rows[i].Tag is not WorldSnapshot row || row.Id != w.Id).Any();

        _updating = true;
        SuspendLayout();
        try
        {
            if (rebuild)
            {
                Rows.Clear();
                if (worlds.Count > 0)
                    Rows.Add(worlds.Count);
            }
            for (int i = 0; i < worlds.Count; i++)
            {
                var world = worlds[i];
                var row = Rows[i];
                row.Tag = world;
                if (_editable)
                {
                    row.Cells["track"].Value = world.IsFavorite || world.IsTracked;
                    row.Cells["track"].ReadOnly = world.IsFavorite || world.World is null;
                    row.Cells["track"].ToolTipText = world.IsFavorite ? "Always-tracked worlds stay enabled. Uncheck Always first." : "Track for this session";
                    row.Cells["favorite"].Value = world.IsFavorite;
                }
                row.Cells["world"].Value = world.Id;
                row.Cells["region"].Value = world.World?.Region ?? "Unavailable";
                row.Cells["access"].Value = world.World is null ? "—" : world.World.Members ? "Members" : "Free";
                row.Cells["activity"].Value = world.World?.Activity is { } activity && activity != "-" ? activity : "—";
                row.Cells["ping"].Value = world.LatestResult?.Status == ProbeStatus.Success
                    ? $"{world.LatestResult.RoundTripMilliseconds} ms" : "—";
                string status = StatusText(world, settings);
                row.Cells["status"].Value = status;
                row.Cells["status"].ToolTipText = status;
                Color color = world.LatestResult is null ? Theme.Muted : world.LatestResult.Status != ProbeStatus.Success
                    ? Theme.Failed : world.LatestResult.IsGood(settings.LatencyCutoffMs) ? Theme.Good : Theme.Slow;
                row.Cells["ping"].Style.ForeColor = color;
                row.Cells["status"].Style.ForeColor = color;
                if (_editable)
                    row.Cells["source"].Value = world.LatestResult is null ? "—" : world.IsScanResult ? "Scan result" : "Live check";
                row.Cells["ping"].ToolTipText = $"Last checked: {world.LastChecked?.ToLocalTime().ToString("G") ?? "Not checked"}";
                if (_editable)
                {
                    row.Cells["checked"].Value = world.LastChecked?.ToLocalTime().ToString("HH:mm:ss") ?? "—";
                    row.Cells["checked"].ToolTipText = world.LastChecked?.ToLocalTime().ToString("G") ?? "Not checked";
                }
            }
            if (rebuild)
            {
                var selection = Rows.Cast<DataGridViewRow>().FirstOrDefault(r => r.Tag is WorldSnapshot w && w.Id == selectedId);
                if (selection is not null)
                    CurrentCell = selection.Cells[column];
                var firstRow = Rows.Cast<DataGridViewRow>().FirstOrDefault(r => r.Tag is WorldSnapshot w && w.Id == firstId);
                if (firstRow is not null)
                    FirstDisplayedScrollingRowIndex = firstRow.Index;
                HorizontalScrollingOffset = horizontalOffset;
            }
        }
        finally
        {
            ResumeLayout();
            _updating = false;
        }
    }

    private static string StatusText(WorldSnapshot world, AppSettings settings)
    {
        if (world.World is null) return "Not in catalogue";
        if (world.Removed && !world.IsFavorite) return "Resting until next review";
        string reading = world.LatestResult?.Status switch
        {
            ProbeStatus.Success => world.LatestResult.IsGood(settings.LatencyCutoffMs) ? "Connected" : "High latency",
            ProbeStatus.TimedOut => "Timed out",
            ProbeStatus.Unreachable => "Unreachable",
            ProbeStatus.DnsError => "DNS lookup failed",
            ProbeStatus.Error => "Probe failed",
            _ => "Waiting for check"
        };
        if (!world.IsTracked) return world.ManualTracking == false ? "Off for this session" : $"Resting · {reading}";
        return world.ConsecutiveBadReadings > 0
            ? $"{reading} · {world.ConsecutiveBadReadings}/{settings.RequiredBadReadings}"
            : reading;
    }
}
