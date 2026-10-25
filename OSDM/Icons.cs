using System.Drawing.Drawing2D;

namespace OSDM;

/// <summary>OSDM's line icons, drawn on a 24x24 grid so they are sharp at any size and in any color.</summary>
static class Icons
{
    static readonly Dictionary<(string, int, int), Bitmap> Cache = new();

    public static Bitmap Get(string name, int size, Color color)
    {
        var key = (name, size, color.ToArgb());
        if (!Cache.TryGetValue(key, out var bmp)) Cache[key] = bmp = Render(name, size, color);
        return bmp;
    }

    static Bitmap Render(string name, int size, Color color)
    {
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.ScaleTransform(size / 24f, size / 24f);
        using var pen = new Pen(color, size <= 18 ? 2.3f : 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        using var tint = new SolidBrush(Color.FromArgb(44, color));
        using var solid = new SolidBrush(color);
        var c = new Canvas(g, pen, tint, solid);

        switch (name)
        {
            case "add":
                c.Circle(12, 12, 9);
                c.Line(12, 8, 12, 16);
                c.Line(8, 12, 16, 12);
                break;
            case "video":
                c.Box(2.5f, 5, 19, 14, 3);
                c.Fill(10, 9, 10, 15, 15.5f, 12);
                break;
            case "resume":
                c.Shape(7, 4.5f, 19.5f, 12, 7, 19.5f);
                break;
            case "pause":
                c.Box(5.5f, 4.5f, 4.5f, 15, 1.5f);
                c.Box(14, 4.5f, 4.5f, 15, 1.5f);
                break;
            case "pauseall":
                c.Circle(12, 12, 9);
                c.Line(10, 9, 10, 15);
                c.Line(14, 9, 14, 15);
                break;
            case "delete":
                c.Shape(5.5f, 7, 6.8f, 20.5f, 17.2f, 20.5f, 18.5f, 7);
                c.Line(3.5f, 7, 20.5f, 7);
                c.Poly(9, 7, 9.5f, 4, 14.5f, 4, 15, 7);
                c.Line(10, 11, 10, 16.5f);
                c.Line(14, 11, 14, 16.5f);
                break;
            case "clear":
                c.Poly(18, 6, 7, 17, 2, 12);
                c.Poly(22, 10, 14.5f, 17.5f, 13, 16);
                break;
            case "folder":
                c.Shape(3, 5, 9, 5, 11, 7.5f, 21, 7.5f, 21, 19, 3, 19);
                break;
            case "settings":
                var gear = new List<float>();
                for (int i = 0; i < 8; i++)
                {
                    double a = i * Math.PI / 4;
                    foreach (var (r, da) in new[] { (7.6, -0.30), (10.0, -0.16), (10.0, 0.16), (7.6, 0.30) })
                    {
                        gear.Add((float)(12 + r * Math.Cos(a + da)));
                        gear.Add((float)(12 + r * Math.Sin(a + da)));
                    }
                }
                c.Shape(gear.ToArray());
                c.Ring(12, 12, 3);
                break;
            case "globe":
                c.Circle(12, 12, 9);
                c.Ellipse(12, 12, 4, 9);
                c.Line(3, 12, 21, 12);
                break;
            case "tools":
                c.Shape(12, 2.5f, 20.5f, 7, 20.5f, 17, 12, 21.5f, 3.5f, 17, 3.5f, 7);
                c.Poly(3.5f, 7, 12, 11.5f, 20.5f, 7);
                c.Line(12, 11.5f, 12, 21.5f);
                break;
            case "info":
                c.Circle(12, 12, 9);
                c.Line(12, 11, 12, 16.5f);
                c.Dot(12, 7.8f, 1.3f);
                break;
            case "exit":
                c.Arc(12, 13, 8, -50, 280);
                c.Line(12, 3, 12, 11.5f);
                break;
            case "restart":
                c.Arc(12, 12, 8, -60, 300);
                c.Poly(3.6f, 4.3f, 8, 5.1f, 6.5f, 9.3f);
                break;
            case "open":
                c.Poly(14, 3, 21, 3, 21, 10);
                c.Line(10, 14, 21, 3);
                c.Poly(18, 14, 18, 20, 4, 20, 4, 6, 10, 6);
                break;
            case "copy":
                c.Box(8, 8, 13, 13, 2);
                c.Poly(8, 16, 3, 16, 3, 3, 16, 3, 16, 8);
                break;
            case "download":
                c.Line(12, 3.5f, 12, 15);
                c.Poly(7, 10, 12, 15, 17, 10);
                c.Poly(3.5f, 15, 3.5f, 20, 20.5f, 20, 20.5f, 15);
                break;
            case "file":
            case "document":
                c.Shape(5, 2.5f, 14, 2.5f, 19, 7.5f, 19, 21.5f, 5, 21.5f);
                c.Poly(14, 2.5f, 14, 7.5f, 19, 7.5f);
                if (name == "document")
                {
                    c.Line(8.5f, 12.5f, 15.5f, 12.5f);
                    c.Line(8.5f, 16.5f, 15.5f, 16.5f);
                }
                break;
            case "inbox":
                c.Shape(2.5f, 12, 5.8f, 4.5f, 18.2f, 4.5f, 21.5f, 12, 21.5f, 19.5f, 2.5f, 19.5f);
                c.Poly(2.5f, 12, 8, 12, 10, 15, 14, 15, 16, 12, 21.5f, 12);
                break;
            case "unfinished":
                c.Circle(12, 12, 9);
                c.Line(12, 7.5f, 12, 16);
                c.Poly(8, 12.5f, 12, 16.5f, 16, 12.5f);
                break;
            case "finished":
                c.Circle(12, 12, 9);
                c.Poly(8, 12.5f, 11, 15.5f, 16.5f, 9.5f);
                break;
            case "archive":
                c.Box(2.5f, 3.5f, 19, 5, 1.5f);
                c.Shape(4, 8.5f, 4, 20.5f, 20, 20.5f, 20, 8.5f);
                c.Line(10, 13, 14, 13);
                break;
            case "music":
                c.Circle(6, 18, 3);
                c.Circle(18, 16, 3);
                c.Poly(9, 18, 9, 5, 21, 3, 21, 16);
                break;
            case "program":
                c.Box(2.5f, 4, 19, 16, 2.5f);
                c.Line(2.5f, 9, 21.5f, 9);
                c.Dot(6, 6.5f, 1);
                c.Dot(9, 6.5f, 1);
                break;
        }
        return bmp;
    }

    /// <summary>Drawing calls in 24x24 grid units: outlines use the pen, shapes get a light tint inside.</summary>
    readonly record struct Canvas(Graphics G, Pen Pen, Brush Tint, Brush Solid)
    {
        public void Line(float x1, float y1, float x2, float y2) => G.DrawLine(Pen, x1, y1, x2, y2);

        public void Poly(params float[] xy) => G.DrawLines(Pen, Points(xy));

        public void Shape(params float[] xy)
        {
            var points = Points(xy);
            G.FillPolygon(Tint, points);
            G.DrawPolygon(Pen, points);
        }

        public void Fill(params float[] xy) => G.FillPolygon(Solid, Points(xy));

        public void Circle(float cx, float cy, float r)
        {
            var rect = new RectangleF(cx - r, cy - r, 2 * r, 2 * r);
            G.FillEllipse(Tint, rect);
            G.DrawEllipse(Pen, rect);
        }

        public void Ring(float cx, float cy, float r) => G.DrawEllipse(Pen, cx - r, cy - r, 2 * r, 2 * r);

        public void Ellipse(float cx, float cy, float rx, float ry) => G.DrawEllipse(Pen, cx - rx, cy - ry, 2 * rx, 2 * ry);

        public void Dot(float cx, float cy, float r) => G.FillEllipse(Solid, cx - r, cy - r, 2 * r, 2 * r);

        public void Box(float x, float y, float w, float h, float r)
        {
            using var path = Shapes.Rounded(new RectangleF(x, y, w, h), r);
            G.FillPath(Tint, path);
            G.DrawPath(Pen, path);
        }

        public void Arc(float cx, float cy, float r, float start, float sweep) => G.DrawArc(Pen, cx - r, cy - r, 2 * r, 2 * r, start, sweep);

        static PointF[] Points(float[] xy)
        {
            var points = new PointF[xy.Length / 2];
            for (int i = 0; i < points.Length; i++) points[i] = new PointF(xy[2 * i], xy[2 * i + 1]);
            return points;
        }
    }
}
