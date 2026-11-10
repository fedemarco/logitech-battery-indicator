using System.Runtime.InteropServices;

namespace LogitechBattery;

/// <summary>Rounded corners and a system border for a borderless window. DWM draws both on Windows 11; Windows 10 has no such attribute.</summary>
internal static class WindowCorners
{
    private const int FirstWindows11Build = 22000;
    private const int CornerPreferenceAttribute = 33; // DWMWA_WINDOW_CORNER_PREFERENCE
    private const int BorderColorAttribute = 34; // DWMWA_BORDER_COLOR
    private const int RoundCorners = 2; // DWMWCP_ROUND
    private const int Success = 0; // S_OK

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int valueSize);

    /// <summary>True when the system now draws the rounded corners and the border, so the caller must not draw its own border.</summary>
    public static bool TryUseRoundedCorners(IntPtr handle, Color borderColor)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, FirstWindows11Build))
            return false;

        try
        {
            var cornerPreference = RoundCorners;
            if (DwmSetWindowAttribute(handle, CornerPreferenceAttribute, ref cornerPreference, sizeof(int)) != Success)
                return false;

            var colorRef = borderColor.R | (borderColor.G << 8) | (borderColor.B << 16); // COLORREF is 0x00BBGGRR
            return DwmSetWindowAttribute(handle, BorderColorAttribute, ref colorRef, sizeof(int)) == Success;
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }
}
