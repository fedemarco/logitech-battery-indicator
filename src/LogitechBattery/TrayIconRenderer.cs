using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using LogitechBattery.Battery;

namespace LogitechBattery;

/// <summary>Draws a <see cref="TrayIndicator"/> from the embedded razer-taskbar gauge icons (32x32 PNG).</summary>
internal static class TrayIconRenderer
{
    private static readonly Dictionary<string, Bitmap> Cache = [];

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    // shortcut: icon size taken at startup; redraw on DPI change if users switch scaling
    public static Icon Render(TrayIndicator indicator, int size)
    {
        var source = Load(indicator);

        using var bitmap = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            // Bilinear + half-pixel offset averages each 2x2 block exactly at 32 -> 16, which keeps pixel-art lines
            // crisp without assuming the art is 2-px aligned; other sizes still scale smoothly.
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(source, new Rectangle(0, 0, size, size));
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

    private static Bitmap Load(TrayIndicator indicator)
    {
        var key = $"{indicator.Icon}|{indicator.Tint}";
        if (Cache.TryGetValue(key, out var cached))
            return cached;

        using var stream = typeof(TrayIconRenderer).Assembly.GetManifestResourceStream(indicator.Icon + ".png")
            ?? throw new InvalidOperationException($"Missing icon resource {indicator.Icon}");
        using var png = new Bitmap(stream);
        var bitmap = new Bitmap(png.Width, png.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
            g.DrawImageUnscaled(png, 0, 0);

        if (indicator.Tint is IndicatorTint.Orange or IndicatorTint.Red)
            Recolor(bitmap, indicator.Tint == IndicatorTint.Orange ? Color.FromArgb(245, 158, 11) : Color.FromArgb(220, 38, 38));

        return Cache[key] = bitmap;
    }

    /// <summary>Replaces green pixels (G clearly above R and B) with <paramref name="target"/>, keeping brightness and alpha.</summary>
    private static void Recolor(Bitmap bitmap, Color target)
    {
        for (var y = 0; y < bitmap.Height; y++)
            for (var x = 0; x < bitmap.Width; x++)
            {
                var c = bitmap.GetPixel(x, y);
                if (c.A == 0 || c.G < c.R + 40 || c.G < c.B + 40)
                    continue; // transparent or the dark fill
                var k = c.G / 255f; // brightness relative to a full-intensity green
                bitmap.SetPixel(x, y, Color.FromArgb(c.A, (int)(target.R * k), (int)(target.G * k), (int)(target.B * k)));
            }
    }
}
