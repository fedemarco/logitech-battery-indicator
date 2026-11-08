using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using LogitechBattery.Battery;

namespace LogitechBattery;

/// <summary>Draws a <see cref="TrayIndicator"/> from the embedded razer-taskbar gauge icons (32x32 PNG), colors as-is.</summary>
internal static class TrayIconRenderer
{
    // Only the UI thread renders icons (TrayApplicationContext.UpdateTray), so the cache needs no lock.
    private static readonly Dictionary<string, Bitmap> Cache = [];

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    public static Icon Render(TrayIndicator indicator, int size)
    {
        using var bitmap = Draw(Load(indicator.Icon), size);
        return ToIcon(bitmap);
    }

    private static Bitmap Draw(Bitmap source, int size)
    {
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        // The art is drawn in 2x2 blocks, so nearest-neighbor at 32 -> 16 is pixel-exact. At 20, 24 and 28 px some pixels become
        // 1 px wider than others, which still looks cleaner than smoothing (compared on previews, 2026-11-08).
        graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        graphics.PixelOffsetMode = PixelOffsetMode.Half;
        graphics.DrawImage(source, new Rectangle(0, 0, size, size));
        return bitmap;
    }

    private static Icon ToIcon(Bitmap bitmap)
    {
        var handle = bitmap.GetHicon();
        try
        {
            using var temp = Icon.FromHandle(handle);
            return (Icon)temp.Clone(); // the clone owns its own data, so the handle can be freed
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private static Bitmap Load(string icon)
    {
        if (Cache.TryGetValue(icon, out var cached))
            return cached;

        using var stream = typeof(TrayIconRenderer).Assembly.GetManifestResourceStream(icon + ".png")
            ?? throw new InvalidOperationException($"Missing icon resource {icon}");
        using var png = new Bitmap(stream);
        var copy = new Bitmap(png); // a Bitmap needs its stream alive, the copy doesn't
        Cache[icon] = copy;
        return copy;
    }
}
