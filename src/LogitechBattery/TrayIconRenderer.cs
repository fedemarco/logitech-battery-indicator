using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using LogitechBattery.Battery;

namespace LogitechBattery;

/// <summary>Draws a <see cref="TrayIndicator"/> as a colored rounded-square badge with the number on it.</summary>
internal static class TrayIconRenderer
{
    private static readonly Color Dark = Color.FromArgb(30, 30, 30);

    // Bolt outline in a 0..1 box.
    private static readonly PointF[] Bolt =
    [
        new(0.62f, 0f), new(0.12f, 0.58f), new(0.45f, 0.58f),
        new(0.34f, 1f), new(0.9f, 0.38f), new(0.55f, 0.38f),
    ];

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    // shortcut: icon size taken at startup; redraw on DPI change if users switch scaling
    public static Icon Render(TrayIndicator indicator, int size)
    {
        using var bitmap = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            var (back, fore) = Colors(indicator.Color);
            using (var badge = RoundedRect(new RectangleF(0, 0, size, size), size * 0.22f))
            using (var brush = new SolidBrush(back))
                g.FillPath(brush, badge);

            DrawText(g, indicator.Text, size, fore);

            if (indicator.Charging)
            {
                var box = size * 0.55f;
                using var path = new GraphicsPath();
                path.AddPolygon(Bolt.Select(p => new PointF(size - box + p.X * box, p.Y * box)).ToArray());
                using var fill = new SolidBrush(Color.FromArgb(255, 221, 51));
                using var outline = new Pen(Dark, Math.Max(1f, size / 16f)) { LineJoin = LineJoin.Round };
                g.DrawPath(outline, path);
                g.FillPath(fill, path);
            }
        }

        var hIcon = bitmap.GetHicon();
        try
        {
            using var temp = Icon.FromHandle(hIcon);
            return (Icon)temp.Clone(); // the clone owns its own data, so the handle can be freed
        }
        finally
        {
            DestroyIcon(hIcon);
        }
    }

    private static (Color Back, Color Fore) Colors(IndicatorColor color) => color switch
    {
        IndicatorColor.Green => (Color.FromArgb(21, 128, 61), Color.White),
        IndicatorColor.Orange => (Color.FromArgb(245, 158, 11), Dark), // white is too weak on orange
        IndicatorColor.Red => (Color.FromArgb(220, 38, 38), Color.White),
        _ => (Color.FromArgb(107, 114, 128), Color.White),
    };

    private static void DrawText(Graphics g, string text, int size, Color color)
    {
        using var path = new GraphicsPath();
        using var family = new FontFamily("Segoe UI");
        path.AddString(text, family, (int)FontStyle.Bold, size * 0.78f, PointF.Empty, StringFormat.GenericTypographic);

        var bounds = path.GetBounds();
        // Condense horizontally (keeping height) when the digits are wider than the badge, e.g. "100" at 16 px.
        var maxWidth = size - Math.Max(2f, size * 0.1f);
        var sx = Math.Min(1f, maxWidth / bounds.Width);

        using var m = new Matrix();
        m.Translate(size / 2f, size / 2f);
        m.Scale(sx, 1f);
        m.Translate(-(bounds.X + bounds.Width / 2f), -(bounds.Y + bounds.Height / 2f));
        path.Transform(m);

        using var brush = new SolidBrush(color);
        g.FillPath(brush, path);
    }

    private static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        var d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
