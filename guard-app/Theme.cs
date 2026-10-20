namespace BcpGuardApp;

/// <summary>Dark colors matching the BCP website.</summary>
static class Theme
{
    public static readonly Color Bg      = Color.FromArgb(17, 20, 26);
    public static readonly Color Panel   = Color.FromArgb(32, 37, 45);
    public static readonly Color Field   = Color.FromArgb(15, 18, 23);
    public static readonly Color Text    = Color.FromArgb(230, 234, 238);
    public static readonly Color Muted   = Color.FromArgb(150, 160, 172);
    public static readonly Color Accent  = Color.FromArgb(239, 167, 47);
    public static readonly Color Green   = Color.FromArgb(31, 111, 67);
    public static readonly Color Red     = Color.FromArgb(138, 47, 47);
    public static readonly Color OkText  = Color.FromArgb(85, 201, 138);
    public static readonly Color BadText = Color.FromArgb(242, 124, 124);

    public static void StyleButton(Button b, Color back, Color fore)
    {
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderSize = 0;
        b.BackColor = back;
        b.ForeColor = fore;
        b.Cursor = Cursors.Hand;
        b.Font = new Font("Segoe UI", 10.5f, FontStyle.Bold);
    }

    public static void StyleField(TextBox t)
    {
        t.BackColor = Field;
        t.ForeColor = Text;
        t.BorderStyle = BorderStyle.FixedSingle;
    }
}
