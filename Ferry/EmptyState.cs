namespace Ferry;

/// <summary>Shown in place of the download list when it has nothing to show.</summary>
sealed class EmptyState : Control
{
    string _title = "", _hint = "";
    Font? _titleFont;

    public EmptyState()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        TabStop = false;
    }

    public void SetText(string title, string hint)
    {
        if (title == _title && hint == _hint) return;
        _title = title;
        _hint = hint;
        Invalidate();
    }

    int S(int px) => LogicalToDeviceUnits(px);
    Font TitleFont => _titleFont ??= new Font(Font.FontFamily, Font.Size * 1.35f, FontStyle.Bold);

    protected override void OnPaint(PaintEventArgs e)
    {
        var t = Theme.Current;
        var g = e.Graphics;
        g.Clear(t.Content);

        int circle = S(88), icon = S(40);
        int top = Math.Max(S(16), (Height - circle - S(110)) / 2);
        var c = new Rectangle((Width - circle) / 2, top, circle, circle);
        Shapes.FillCircle(g, t.Selection, c);
        g.DrawImage(Icons.Get("download", icon, t.Accent), new Rectangle(c.X + (circle - icon) / 2, c.Y + (circle - icon) / 2, icon, icon));

        var title = new Rectangle(S(20), c.Bottom + S(20), Width - S(40), S(30));
        TextRenderer.DrawText(g, _title, TitleFont, title, t.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.Top);
        var hint = new Rectangle(S(40), title.Bottom + S(2), Width - S(80), S(48));
        TextRenderer.DrawText(g, _hint, Font, hint, t.TextMuted, TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.WordBreak);
    }

    protected override void OnFontChanged(EventArgs e)
    {
        _titleFont?.Dispose();
        _titleFont = null;
        base.OnFontChanged(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _titleFont?.Dispose();
        base.Dispose(disposing);
    }
}
