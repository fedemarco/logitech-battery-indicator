using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using LogitechBattery.Battery;

namespace LogitechBattery;

/// <summary>Draws a <see cref="TrayIndicator"/> from the embedded razer-taskbar gauge icons (32x32 PNG), colors as-is.</summary>
internal static class TrayIconRenderer
{
    private static readonly Dictionary<string, Bitmap> Cache = [];

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    // shortcut: icon size taken at startup; redraw on DPI change if users switch scaling
    public static Icon Render(TrayIndicator indicator, int size)
    {
        var source = Load(indicator.Icon);

        using var bitmap = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            // The art is drawn in 2x2 blocks, so nearest-neighbor at 32 -> 16 is pixel-exact.
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
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

    private static Bitmap Load(string icon)
    {
        if (Cache.TryGetValue(icon, out var cached))
            return cached;

        using var stream = typeof(TrayIconRenderer).Assembly.GetManifestResourceStream(icon + ".png")
            ?? throw new InvalidOperationException($"Missing icon resource {icon}");
        using var png = new Bitmap(stream);
        return Cache[icon] = new Bitmap(png); // copy: a Bitmap needs its stream alive, this one doesn't
    }
}
