using Ferry.Core;

namespace Ferry;

/// <summary>Bottom panel: details of the selected download, and a bar showing what each connection has downloaded.</summary>
sealed class DetailsPanel : Control
{
    const TextFormatFlags Line = TextFormatFlags.SingleLine | TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;

    DownloadItem? _item;
    Font? _titleFont, _smallFont;

    public DetailsPanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        TabStop = false;
    }

    /// <summary>The download to show. Setting it, even to the same one, repaints with fresh numbers.</summary>
    public DownloadItem? Item
    {
        get => _item;
        set { _item = value; Invalidate(); }
    }

    int S(int px) => LogicalToDeviceUnits(px);
    Font TitleFont => _titleFont ??= new Font(Font.FontFamily, Font.Size * 1.15f, FontStyle.Bold);
    Font SmallFont => _smallFont ??= new Font(Font.FontFamily, Font.Size * 0.85f);

    protected override void OnPaint(PaintEventArgs e)
    {
        var t = Theme.Current;
        var g = e.Graphics;
        g.Clear(t.Content);
        using (var pen = new Pen(t.Border)) g.DrawLine(pen, 0, 0, Width, 0);

        var item = _item;
        if (item == null)
        {
            TextRenderer.DrawText(g, "Select a download to see its details here", Font, ClientRectangle, t.TextMuted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }

        int pad = S(20), width = Width - 2 * pad, y = S(12);
        TextRenderer.DrawText(g, Format.DisplayName(item), TitleFont, new Rectangle(pad, y, width, S(24)), t.Text, Line);
        y += S(24);
        TextRenderer.DrawText(g, item.Url, Font, new Rectangle(pad, y, width, S(20)), t.TextMuted, Line);
        y += S(30);

        // Facts in columns; the folder gets a double-width column at the end.
        var facts = Facts(item);
        int unit = width / (facts.Count + 1);
        int x = pad;
        for (int i = 0; i < facts.Count; i++)
        {
            bool last = i == facts.Count - 1;
            int w = (last ? unit * 2 : unit) - S(12);
            TextRenderer.DrawText(g, facts[i].Label.ToUpperInvariant(), SmallFont, new Rectangle(x, y, w, S(16)), t.TextMuted, Line);
            TextRenderer.DrawText(g, facts[i].Value, Font, new Rectangle(x, y + S(17), w, S(20)), t.Text,
                last ? Line & ~TextFormatFlags.EndEllipsis | TextFormatFlags.PathEllipsis : Line);
            x += w + S(12);
        }
        y += S(46);

        DrawConnections(g, item, new Rectangle(pad, y, width, S(10)), t);
    }

    static List<(string Label, string Value)> Facts(DownloadItem item)
    {
        bool active = item.Status is DownloadStatus.Downloading or DownloadStatus.Merging;
        long done = item.Status == DownloadStatus.Completed && item.TotalSize > 0 ? item.TotalSize : item.Downloaded;
        var facts = new List<(string, string)>
        {
            ("Downloaded", item.TotalSize > 0 ? $"{Format.Size(done)} of {Format.Size(item.TotalSize)}" : Format.Size(done)),
            ("Speed", active && item.Speed >= 1 ? Format.Speed(item.Speed) : "-"),
            ("Time left", Format.TimeLeft(item) is { Length: > 0 } left ? left : "-"),
        };
        if (item.Kind == DownloadKind.Video)
        {
            facts.Add(("Quality", item.Quality ?? "Default (Settings)"));
        }
        else
        {
            facts.Add(("Connections", active ? item.ActiveConnections.ToString() : "-"));
            facts.Add(("Resume", item.Segments.Count == 0 ? "-" : item.ResumeSupported ? "Supported" : "Not supported"));
        }
        facts.Add(("Saved to", item.Folder.Length > 0 ? item.Folder : "-"));
        return facts;
    }

    /// <summary>The file as one bar; each connection's range is a block, filled as far as that connection got.</summary>
    void DrawConnections(Graphics g, DownloadItem item, Rectangle bar, Theme t)
    {
        var color = t.StatusColor(item.Status);
        Shapes.FillRounded(g, t.Track, bar, bar.Height / 2f);

        List<(long Start, long Done)> parts;
        lock (item.Segments) parts = item.Segments.Select(s => (s.Start, s.Downloaded)).ToList();
        double total = item.TotalSize;

        if (item.Kind == DownloadKind.Video || total <= 0 || parts.Count == 0)
        {
            double progress = item.Status == DownloadStatus.Completed ? 1 : item.Progress;
            if (progress > 0) Shapes.FillRounded(g, color, bar with { Width = Math.Max(bar.Height, (int)(bar.Width * progress)) }, bar.Height / 2f);
            return;
        }

        var state = g.Save();
        using (var clip = Shapes.Rounded(bar, bar.Height / 2f)) g.SetClip(clip);
        using (var brush = new SolidBrush(color))
            foreach (var (start, done) in parts)
                g.FillRectangle(brush, bar.X + (float)(start / total * bar.Width), bar.Y, (float)(done / total * bar.Width), bar.Height);
        using (var gap = new Pen(t.Content, Math.Max(1, S(2))))
            foreach (var (start, _) in parts.Where(p => p.Start > 0))
            {
                float x = bar.X + (float)(start / total * bar.Width);
                g.DrawLine(gap, x, bar.Y, x, bar.Bottom);
            }
        g.Restore(state);

        string caption = parts.Count == 1 ? "1 part" : $"{parts.Count} parts, one per connection";
        TextRenderer.DrawText(g, caption, SmallFont, new Rectangle(bar.X, bar.Bottom + S(4), bar.Width, S(16)), t.TextMuted, Line);
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
        }
        base.Dispose(disposing);
    }
}
