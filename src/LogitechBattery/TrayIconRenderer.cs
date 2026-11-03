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
            var margin = Math.Max(1, size / 16);
            using (var badge = RoundedRect(new RectangleF(margin, margin, size - 2 * margin, size - 2 * margin), size * 0.2f))
            using (var brush = new SolidBrush(back))
                g.FillPath(brush, badge);

            DrawText(g, indicator.Text, size, margin, fore);

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
        IndicatorColor.Green => (Color.FromArgb(22, 163, 74), Color.White),
        IndicatorColor.Orange => (Color.FromArgb(245, 158, 11), Dark), // white is too weak on orange
        IndicatorColor.Red => (Color.FromArgb(220, 38, 38), Color.White),
        _ => (Color.FromArgb(107, 114, 128), Color.White),
    };

    // Families are looked up once. GDI+ lists Bahnschrift's named instances as separate (31-char truncated) names.
    private static readonly Lazy<FontFamily> NarrowFont = new(() => FindFamily(
        "Bahnschrift SemiBold SemiConden", "Bahnschrift SemiCondensed"));
    private static readonly Lazy<FontFamily> NarrowestFont = new(() => FindFamily(
        "Bahnschrift SemiBold Condensed", "Bahnschrift Condensed", "Bahnschrift SemiBold SemiConden", "Bahnschrift SemiCondensed"));

    private static FontFamily FindFamily(params string[] names)
    {
        using var installed = new InstalledFontCollection();
        foreach (var name in names.Append("Segoe UI"))
            foreach (var f in installed.Families)
                if (f.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return f;
        return FontFamily.GenericSansSerif; // never throws if Windows fonts are missing
    }

    private static void DrawText(Graphics g, string text, int size, int margin, Color color)
    {
        // Grid-fit (hinted) AA; ClearType would fringe on the transparent bitmap.
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        var family = text.Length >= 3 ? NarrowestFont.Value : NarrowFont.Value;
        var style = family.IsStyleAvailable(FontStyle.Regular) ? FontStyle.Regular : FontStyle.Bold;
        using var format = StringFormat.GenericTypographic; // returns a new instance each call

        var innerWidth = size - 2 * margin - 1f; // 1 px breathing room inside the badge
        var innerHeight = size - 2 * margin - 2f;
        const float DigitHeight = 0.7f; // digit height ~ cap height, as a fraction of the em
        var px = size;
        Font? font = null;
        SizeF measured = default;
        for (; px > 4; px--)
        {
            font?.Dispose();
            font = new Font(family, px, style, GraphicsUnit.Pixel);
            measured = g.MeasureString(text, font, PointF.Empty, format);
            if (measured.Width <= innerWidth && px * DigitHeight <= innerHeight) break;
        }

        using (font)
        {
            // Center the digits' height (no descenders), not the line height; baseline = digit bottom.
            var ascent = px * family.GetCellAscent(style) / family.GetEmHeight(style);
            var baseline = size / 2f + px * DigitHeight / 2f;
            var x = MathF.Round((size - measured.Width) / 2f);
            var y = MathF.Round(baseline - ascent);
            using var brush = new SolidBrush(color);
            g.DrawString(text, font!, brush, x, y, format);
        }
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
