using System.Drawing.Drawing2D;

namespace OSDM;

/// <summary>Flat, themed look for the menu bar, toolbar, status bar and menus. Reads <see cref="Theme.Current"/> on every paint.</summary>
sealed class FlatRenderer : ToolStripProfessionalRenderer
{
    public FlatRenderer() : base(new Colors()) => RoundedEdges = false;

    static Theme T => Theme.Current;

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        using var brush = new SolidBrush(e.ToolStrip is ToolStripDropDown ? T.Content : T.Background);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        using var pen = new Pen(T.Border);
        var size = e.ToolStrip.Size;
        switch (e.ToolStrip)
        {
            case ToolStripDropDown:
                e.Graphics.DrawRectangle(pen, 0, 0, size.Width - 1, size.Height - 1);
                break;
            case StatusStrip:
                e.Graphics.DrawLine(pen, 0, 0, size.Width, 0);
                break;
            case MenuStrip:
                break;
            default:
                e.Graphics.DrawLine(pen, 0, size.Height - 1, size.Width, size.Height - 1);
                break;
        }
    }

    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
    {
        using var brush = new SolidBrush(T.Content);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Enabled || !(e.Item.Selected || e.Item.Pressed)) return;
        var r = new Rectangle(Point.Empty, e.Item.Size);
        r.Inflate(e.Item.IsOnDropDown ? -4 : -1, -1);
        Shapes.FillRounded(e.Graphics, e.Item.Pressed && !e.Item.IsOnDropDown ? T.Selection : T.Hover, r, 4);
    }

    protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
    {
        var button = e.Item as ToolStripButton;
        Color? color = !e.Item.Enabled ? null
            : e.Item.Pressed || button?.Checked == true ? T.Selection
            : e.Item.Selected ? T.Hover
            : null;
        if (color != null) Shapes.FillRounded(e.Graphics, color.Value, new Rectangle(1, 1, e.Item.Width - 2, e.Item.Height - 2), 6);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        using var pen = new Pen(T.Border);
        var s = e.Item.Size;
        if (e.Vertical) e.Graphics.DrawLine(pen, s.Width / 2, s.Height / 5, s.Width / 2, s.Height - s.Height / 5);
        else e.Graphics.DrawLine(pen, 8, s.Height / 2, s.Width - 8, s.Height / 2);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? T.Text : Theme.Mix(T.TextMuted, T.Background, 0.35f);
        base.OnRenderItemText(e);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = T.TextMuted;
        base.OnRenderArrow(e);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        var r = e.ImageRectangle;
        r.Inflate(2, 2);
        Shapes.FillRounded(e.Graphics, T.Selection, r, 4);
        using var pen = new Pen(T.Accent, 2) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        var mode = e.Graphics.SmoothingMode;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        float x = r.X, y = r.Y, w = r.Width, h = r.Height;
        e.Graphics.DrawLines(pen, new[] { new PointF(x + w * 0.25f, y + h * 0.52f), new PointF(x + w * 0.43f, y + h * 0.7f), new PointF(x + w * 0.76f, y + h * 0.32f) });
        e.Graphics.SmoothingMode = mode;
    }

    /// <summary>Colors for the parts the overrides above do not draw themselves.</summary>
    sealed class Colors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => T.Content;
        public override Color ImageMarginGradientBegin => T.Content;
        public override Color ImageMarginGradientMiddle => T.Content;
        public override Color ImageMarginGradientEnd => T.Content;
        public override Color MenuBorder => T.Border;
        public override Color MenuItemBorder => Color.Transparent;
        public override Color MenuItemSelected => T.Hover;
        public override Color MenuStripGradientBegin => T.Background;
        public override Color MenuStripGradientEnd => T.Background;
        public override Color ToolStripGradientBegin => T.Background;
        public override Color ToolStripGradientMiddle => T.Background;
        public override Color ToolStripGradientEnd => T.Background;
        public override Color ToolStripBorder => T.Border;
        public override Color StatusStripGradientBegin => T.Background;
        public override Color StatusStripGradientEnd => T.Background;
        public override Color SeparatorDark => T.Border;
        public override Color SeparatorLight => T.Border;
        public override Color ButtonSelectedHighlight => T.Hover;
        public override Color ButtonSelectedBorder => Color.Transparent;
        public override Color OverflowButtonGradientBegin => T.Background;
        public override Color OverflowButtonGradientMiddle => T.Background;
        public override Color OverflowButtonGradientEnd => T.Background;
    }
}
