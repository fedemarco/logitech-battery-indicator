using System.Drawing.Text;
using System.Runtime.InteropServices;
using LogitechBattery.Battery;
using Microsoft.Win32;

namespace LogitechBattery;

/// <summary>Draws a <see cref="TrayIndicator"/> as a Windows battery glyph from the system icon font.</summary>
internal static class TrayIconRenderer
{
    private static readonly Color Orange = Color.FromArgb(245, 158, 11);
    private static readonly Color Red = Color.FromArgb(220, 38, 38);

    // Looked up once; null if neither font exists (then the app icon is used).
    private static readonly Lazy<FontFamily?> GlyphFont = new(() =>
    {
        using var installed = new InstalledFontCollection();
        foreach (var name in new[] { "Segoe Fluent Icons", "Segoe MDL2 Assets" })
            foreach (var f in installed.Families)
                if (f.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return f;
        return null;
    });

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    /// <summary>True if the taskbar uses the light theme; a missing value counts as dark.</summary>
    public static bool IsTaskbarLight()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("SystemUsesLightTheme") is 1;
    }

    // shortcut: icon size taken at startup; redraw on DPI change if users switch scaling
    public static Icon Render(TrayIndicator indicator, int size, bool lightTaskbar)
    {
        var family = GlyphFont.Value;
        if (family is null)
            return (Icon)SystemIcons.Application.Clone(); // the caller disposes it

        using var bitmap = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            // Grid-fit (hinted) AA; ClearType would fringe on the transparent bitmap.
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            var color = indicator.Tint switch
            {
                IndicatorTint.Orange => Orange,
                IndicatorTint.Red => Red,
                _ => lightTaskbar ? Color.Black : Color.White,
            };
            var text = indicator.Glyph.ToString();
            using var font = new Font(family, size, FontStyle.Regular, GraphicsUnit.Pixel); // fonts are drawn on a 16 px grid
            using var format = StringFormat.GenericTypographic; // returns a new instance each call
            var measured = g.MeasureString(text, font, PointF.Empty, format);
            using var brush = new SolidBrush(color);
            g.DrawString(text, font, brush, MathF.Round((size - measured.Width) / 2f), MathF.Round((size - measured.Height) / 2f), format);
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
}
