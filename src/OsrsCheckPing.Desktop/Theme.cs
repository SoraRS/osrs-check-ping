namespace OsrsCheckPing.Desktop;

internal static class Theme
{
    public static readonly Color Background = Color.FromArgb(25, 25, 22);
    public static readonly Color Panel = Color.FromArgb(35, 35, 30);
    public static readonly Color Alternate = Color.FromArgb(39, 39, 33);
    public static readonly Color Raised = Color.FromArgb(48, 48, 39);
    public static readonly Color Border = Color.FromArgb(70, 66, 52);
    public static readonly Color Gold = Color.FromArgb(211, 185, 124);
    public static readonly Color Text = Color.FromArgb(239, 234, 218);
    public static readonly Color Muted = Color.FromArgb(166, 168, 148);
    public static readonly Color Good = Color.FromArgb(143, 205, 170);
    public static readonly Color Slow = Color.FromArgb(235, 191, 117);
    public static readonly Color Failed = Color.FromArgb(228, 150, 132);

    public static void Apply(Control control)
    {
        control.BackColor = control is Form ? Background
            : control is System.Windows.Forms.Label or SignalEmblem or TableLayoutPanel or FlowLayoutPanel ? Color.Transparent : Panel;
        if (control is not System.Windows.Forms.Label)
            control.ForeColor = Text;
        if (control is Button button)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = Border;
            button.FlatAppearance.MouseOverBackColor = Raised;
            button.FlatAppearance.MouseDownBackColor = Background;
            button.ForeColor = Gold;
            button.AutoSize = true;
            button.Padding = new Padding(13, 6, 13, 6);
            button.Cursor = Cursors.Hand;
        }
        if (control is TextBox textBox)
            textBox.BorderStyle = BorderStyle.FixedSingle;
        if (control is GroupBox)
            control.ForeColor = Gold;
        foreach (Control child in control.Controls)
            Apply(child);
    }

    public static Label Label(string text, bool heading = false) => new()
    {
        Text = text, AutoSize = true, ForeColor = heading ? Gold : Muted,
        BackColor = Color.Transparent, Margin = new Padding(3, 5, 3, 5),
        Font = new Font("Segoe UI", heading ? 13 : 10, heading ? FontStyle.Bold : FontStyle.Regular)
    };

    public static void StyleTabs(TabControl tabs)
    {
        tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
        tabs.SizeMode = TabSizeMode.Fixed;
        tabs.ItemSize = new Size(170, 42);
        tabs.DrawItem += (_, args) =>
        {
            using var brush = new SolidBrush(args.Index == tabs.SelectedIndex ? Panel : Background);
            args.Graphics.FillRectangle(brush, args.Bounds);
            if (args.Index == tabs.SelectedIndex)
            {
                using var accent = new SolidBrush(Gold);
                args.Graphics.FillRectangle(accent, args.Bounds.Left + 12, args.Bounds.Bottom - 3, args.Bounds.Width - 24, 2);
            }
            TextRenderer.DrawText(args.Graphics, tabs.TabPages[args.Index].Text, tabs.Font, args.Bounds,
                args.Index == tabs.SelectedIndex ? Gold : Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        };
    }
}
