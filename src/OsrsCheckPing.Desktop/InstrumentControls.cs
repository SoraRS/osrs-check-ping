using System.Drawing.Drawing2D;

namespace OsrsCheckPing.Desktop;

// Small native vector details keep the interface crisp at any display scale.
internal class RunePanel : Panel
{
    public RunePanel()
    {
        DoubleBuffered = true;
        BackColor = Theme.Panel;
        Padding = new Padding(16);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        float inset = DeviceDpi / 96f;
        float cut = 8 * inset;
        float right = ClientSize.Width - inset;
        float bottom = ClientSize.Height - inset;
        if (right <= cut * 2 || bottom <= cut * 2) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var outline = new Pen(Theme.Border, inset);
        PointF[] corners = [new(cut, inset), new(right - cut, inset), new(right, cut),
            new(right, bottom - cut), new(right - cut, bottom), new(cut, bottom), new(inset, bottom - cut), new(inset, cut)];
        e.Graphics.DrawPolygon(outline, corners);
        using var accent = new Pen(Color.FromArgb(160, Theme.Gold), inset);
        e.Graphics.DrawLine(accent, cut, inset, Math.Min(right - cut, cut + 35 * inset), inset);
    }
}

internal sealed class MetricCard : RunePanel
{
    private readonly Label _value;
    private readonly Label _detail;

    public MetricCard(string caption)
    {
        Padding = new Padding(16, 10, 12, 9);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Color.Transparent };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var title = Theme.Label(caption.ToUpperInvariant());
        title.Font = new Font("Segoe UI", 8, FontStyle.Bold);
        title.Dock = DockStyle.Fill;
        title.Margin = Padding.Empty;
        _value = Theme.Label("—");
        _value.Font = new Font("Segoe UI Semibold", 20, FontStyle.Regular);
        _value.ForeColor = Theme.Text;
        _value.Dock = DockStyle.Fill;
        _value.Margin = Padding.Empty;
        _detail = Theme.Label("");
        _detail.Font = new Font("Segoe UI", 9);
        _detail.Dock = DockStyle.Fill;
        _detail.Margin = Padding.Empty;
        layout.Controls.Add(title, 0, 0);
        layout.Controls.Add(_value, 0, 1);
        layout.Controls.Add(_detail, 0, 2);
        Controls.Add(layout);
        AccessibleName = caption;
    }

    public void SetValue(string value, string detail, Color? color = null)
    {
        _value.Text = value;
        _value.ForeColor = color ?? Theme.Text;
        _detail.Text = detail;
        AccessibleDescription = $"{value}. {detail}";
    }
}

internal sealed class SignalEmblem : Control
{
    private Color _signalColor = Theme.Gold;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Color SignalColor
    {
        get => _signalColor;
        set { if (_signalColor != value) { _signalColor = value; Invalidate(); } }
    }

    public SignalEmblem()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        TabStop = false;
        AccessibleName = "Connection compass";
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (Width < 8 || Height < 8) return;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float size = Math.Min(Width, Height);
        var state = g.Save();
        g.TranslateTransform((Width - size) / 2, (Height - size) / 2);
        g.ScaleTransform(size / 100, size / 100);
        using var subtle = new Pen(Theme.Border, 1);
        using var gold = new Pen(Theme.Gold, 1.2f);
        using var signal = new Pen(SignalColor, 1.3f);
        using var glow = new SolidBrush(Color.FromArgb(20, SignalColor));
        using var dot = new SolidBrush(SignalColor);
        g.FillEllipse(glow, 13, 13, 74, 74);
        g.DrawEllipse(subtle, 9, 9, 82, 82);
        g.DrawEllipse(gold, 21, 21, 58, 58);
        g.DrawEllipse(subtle, 36, 21, 28, 58);
        g.DrawEllipse(subtle, 21, 37, 58, 26);
        g.DrawLine(subtle, 21, 50, 79, 50);
        g.DrawLine(subtle, 50, 21, 50, 79);
        g.DrawPolygon(gold, [new PointF(50, 2), new(54, 10), new(50, 18), new(46, 10)]);
        g.DrawLine(gold, 50, 83, 50, 96);
        g.DrawLine(gold, 4, 50, 17, 50);
        g.DrawLine(gold, 83, 50, 96, 50);
        g.DrawLines(signal, [new PointF(25, 58), new(36, 58), new(43, 42), new(51, 65), new(59, 33), new(65, 50), new(77, 50)]);
        g.FillEllipse(dot, 73, 46, 8, 8);
        g.Restore(state);
    }
}
