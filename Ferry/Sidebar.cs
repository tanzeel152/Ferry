namespace Ferry;

/// <summary>Left navigation: the Ferry logo, then the download categories with how many downloads each has.</summary>
sealed class Sidebar : Control
{
    /// <summary>A clickable entry, or a section heading when <see cref="Key"/> is null.</summary>
    public sealed record Entry(string? Key, string Text, string Icon = "", Func<Theme, Color>? IconColor = null);

    readonly List<Entry> _entries;
    readonly List<Rectangle> _bounds = new();
    readonly Icon _appIcon;
    Dictionary<string, int> _counts = new();
    string _selected;
    int _hover = -1, _headerHeight;
    Font? _titleFont, _smallFont;
    Bitmap? _logo;

    public event Action? SelectedChanged;

    public Sidebar(IEnumerable<Entry> entries, Icon appIcon)
    {
        _entries = entries.ToList();
        _appIcon = appIcon;
        _selected = _entries.First(e => e.Key != null).Key!;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        TabStop = false;
    }

    public string SelectedKey
    {
        get => _selected;
        set
        {
            if (_selected == value) return;
            _selected = value;
            Invalidate();
            SelectedChanged?.Invoke();
        }
    }

    public string SelectedText => _entries.First(e => e.Key == _selected).Text;

    /// <summary>Height of the logo band; kept equal to the toolbar's so their bottom edges line up.</summary>
    public int HeaderHeight
    {
        get => _headerHeight;
        set { _headerHeight = value; Invalidate(); }
    }

    public void SetCounts(Dictionary<string, int> counts)
    {
        if (counts.Count == _counts.Count && counts.All(p => _counts.TryGetValue(p.Key, out int n) && n == p.Value)) return;
        _counts = counts;
        Invalidate();
    }

    int S(int px) => LogicalToDeviceUnits(px);
    int Header => Math.Max(_headerHeight, S(56));
    Font TitleFont => _titleFont ??= new Font(Font.FontFamily, Font.Size * 1.3f, FontStyle.Bold);
    Font SmallFont => _smallFont ??= new Font(Font.FontFamily, Font.Size * 0.85f);

    void Arrange()
    {
        _bounds.Clear();
        int y = Header + S(10);
        foreach (var entry in _entries)
        {
            int h = entry.Key == null ? S(32) : S(34);
            _bounds.Add(new Rectangle(S(8), y, Width - S(16), h));
            y += h + (entry.Key == null ? 0 : S(2));
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var t = Theme.Current;
        var g = e.Graphics;
        Arrange();
        g.Clear(t.Sidebar);

        // Logo band, level with the toolbar.
        using (var back = new SolidBrush(t.Background)) g.FillRectangle(back, 0, 0, Width, Header);
        int logo = S(32);
        if (_logo == null || _logo.Width != logo)
        {
            _logo?.Dispose();
            using var icon = new Icon(_appIcon, logo, logo);
            _logo = icon.ToBitmap();
        }
        var logoRect = new Rectangle(S(16), (Header - logo) / 2, logo, logo);
        g.DrawImage(_logo, logoRect);
        int textX = logoRect.Right + S(10);
        TextRenderer.DrawText(g, "Ferry", TitleFont, new Rectangle(textX, logoRect.Y - S(3), Width - textX, logo / 2 + S(4)), t.Text,
            TextFormatFlags.Left | TextFormatFlags.Bottom | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, "Download Manager", SmallFont, new Rectangle(textX, logoRect.Y + logo / 2 + S(1), Width - textX, logo / 2), t.TextMuted,
            TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPadding);

        using (var pen = new Pen(t.Border))
        {
            g.DrawLine(pen, 0, Header - 1, Width, Header - 1);
            g.DrawLine(pen, Width - 1, 0, Width - 1, Height);
        }

        for (int i = 0; i < _entries.Count; i++)
        {
            var entry = _entries[i];
            var r = _bounds[i];
            if (entry.Key == null)
            {
                TextRenderer.DrawText(g, entry.Text.ToUpperInvariant(), SmallFont, new Rectangle(r.X + S(12), r.Y, r.Width - S(12), r.Height - S(4)), t.TextMuted,
                    TextFormatFlags.Left | TextFormatFlags.Bottom);
                continue;
            }

            if (entry.Key == _selected)
            {
                Shapes.FillRounded(g, t.Selection, r, S(6));
                Shapes.FillRounded(g, t.Accent, new Rectangle(r.X, r.Y + S(9), S(3), r.Height - S(18)), S(2));
            }
            else if (i == _hover)
            {
                Shapes.FillRounded(g, t.Hover, r, S(6));
            }

            int icon = S(18);
            var iconRect = new Rectangle(r.X + S(12), r.Y + (r.Height - icon) / 2, icon, icon);
            g.DrawImage(Icons.Get(entry.Icon, icon, entry.IconColor?.Invoke(t) ?? t.TextMuted), iconRect);

            int countWidth = S(40);
            var textRect = new Rectangle(iconRect.Right + S(10), r.Y, r.Right - iconRect.Right - S(10) - countWidth, r.Height);
            TextRenderer.DrawText(g, entry.Text, Font, textRect, t.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if (_counts.TryGetValue(entry.Key, out int count) && count > 0)
                TextRenderer.DrawText(g, count.ToString(), Font, new Rectangle(r.Right - countWidth - S(8), r.Y, countWidth, r.Height), t.TextMuted,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
        }
    }

    int HitTest(Point p)
    {
        Arrange();
        for (int i = 0; i < _entries.Count; i++)
            if (_entries[i].Key != null && _bounds[i].Contains(p)) return i;
        return -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int hit = HitTest(e.Location);
        Cursor = hit >= 0 ? Cursors.Hand : Cursors.Default;
        if (hit == _hover) return;
        _hover = hit;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hover == -1) return;
        _hover = -1;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        int hit = HitTest(e.Location);
        if (hit >= 0 && e.Button == MouseButtons.Left) SelectedKey = _entries[hit].Key!;
    }

    protected override void OnFontChanged(EventArgs e)
    {
        _titleFont?.Dispose();
        _smallFont?.Dispose();
        _titleFont = _smallFont = null;
        base.OnFontChanged(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _titleFont?.Dispose();
            _smallFont?.Dispose();
            _logo?.Dispose();
        }
        base.Dispose(disposing);
    }
}
