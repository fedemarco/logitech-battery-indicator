using System.Drawing;

namespace LogitechBattery;

/// <summary>Where the overlay panel opens: the bottom-right corner of the screen, clear of a bottom taskbar.</summary>
public static class OverlayPlacement
{
    private const int Margin = 8;

    // An auto-hidden taskbar reserves no space, so the working area reaches the screen bottom and the taskbar slides up over the panel.
    // shortcut: assumes a bottom taskbar 48 logical px high (Windows 11 default). A monitor without a taskbar, or a Windows 10
    // taskbar on another edge, also gets the lift (cosmetic: the panel floats higher). Read the real position with
    // SHAppBarMessage(ABM_GETTASKBARPOS) if that matters.
    private const int AutoHiddenTaskbarHeight = 48;

    public static Point Location(Rectangle screenBounds, Rectangle workingArea, Size panel, float dpiScale)
    {
        var bottom = workingArea.Bottom;
        if (bottom == screenBounds.Bottom)
            bottom -= (int)(AutoHiddenTaskbarHeight * dpiScale);

        var margin = (int)(Margin * dpiScale);
        return new Point(workingArea.Right - panel.Width - margin, bottom - panel.Height - margin);
    }
}
