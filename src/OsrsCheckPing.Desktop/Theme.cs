namespace OsrsCheckPing.Desktop;

internal static class Theme
{
    public static readonly Color Background = Color.FromArgb(31, 29, 25);
    public static readonly Color Panel = Color.FromArgb(49, 44, 36);
    public static readonly Color Alternate = Color.FromArgb(43, 39, 33);
    public static readonly Color Gold = Color.FromArgb(213, 181, 105);
    public static readonly Color Text = Color.FromArgb(232, 223, 201);
    public static readonly Color Muted = Color.FromArgb(181, 170, 148);
    public static readonly Color Good = Color.FromArgb(152, 200, 133);
    public static readonly Color Slow = Color.FromArgb(237, 193, 112);
    public static readonly Color Failed = Color.FromArgb(231, 146, 128);

    public static void Apply(Control control)
    {
        control.BackColor = control is Form ? Background : Panel;
        if (control is not System.Windows.Forms.Label)
            control.ForeColor = Text;
        if (control is Button button)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = Gold;
            button.ForeColor = Gold;
            button.AutoSize = true;
            button.Padding = new Padding(9, 4, 9, 4);
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
        tabs.ItemSize = new Size(145, 34);
        tabs.DrawItem += (_, args) =>
        {
            using var brush = new SolidBrush(args.Index == tabs.SelectedIndex ? Panel : Background);
            args.Graphics.FillRectangle(brush, args.Bounds);
            TextRenderer.DrawText(args.Graphics, tabs.TabPages[args.Index].Text, tabs.Font, args.Bounds,
                args.Index == tabs.SelectedIndex ? Gold : Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        };
    }
}
