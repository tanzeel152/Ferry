using System.Drawing.Drawing2D;

namespace OSDM;

/// <summary>Rounded rectangles and status dots, used by the custom-drawn controls.</summary>
static class Shapes
{
    static readonly Dictionary<(int, int), Bitmap> Dots = new();

    public static GraphicsPath Rounded(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        if (d <= 0)
        {
            path.AddRectangle(r);
            return path;
        }
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void FillRounded(Graphics g, Color color, RectangleF r, float radius)
    {
        if (r.Width <= 0 || r.Height <= 0) return;
        var mode = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var path = Rounded(r, radius))
        using (var brush = new SolidBrush(color))
            g.FillPath(brush, path);
        g.SmoothingMode = mode;
    }

    public static void FillCircle(Graphics g, Color color, RectangleF r)
    {
        var mode = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var brush = new SolidBrush(color)) g.FillEllipse(brush, r);
        g.SmoothingMode = mode;
    }

    /// <summary>A small colored circle, for status bar items.</summary>
    public static Bitmap Dot(int size, Color color)
    {
        var key = (size, color.ToArgb());
        if (Dots.TryGetValue(key, out var bmp)) return bmp;
        bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp)) FillCircle(g, color, new RectangleF(0.5f, 0.5f, size - 1, size - 1));
        return Dots[key] = bmp;
    }
}
