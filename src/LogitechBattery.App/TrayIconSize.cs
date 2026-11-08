using System.Runtime.InteropServices;

namespace LogitechBattery;

/// <summary>The size of the small icon the taskbar shows: 16 px at 100 % scaling, 20, 24, 28 and 32 px at 125, 150, 175 and 200 %.</summary>
internal static class TrayIconSize
{
    private const int SmallIconWidthMetric = 49; // SM_CXSMICON
    private const string TaskbarWindowClass = "Shell_TrayWnd";

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string className, string? windowName);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetricsForDpi(int index, uint dpi);

    /// <summary>
    /// Sized for the DPI of the taskbar itself. SystemInformation.SmallIconSize follows the DPI of the system at sign-in,
    /// which is wrong after the scaling changes without a sign-out; it is only the fallback (for example while Explorer restarts).
    /// </summary>
    public static int Current()
    {
        var taskbar = FindWindow(TaskbarWindowClass, null);
        var dpi = taskbar == IntPtr.Zero ? 0 : GetDpiForWindow(taskbar);
        var size = dpi == 0 ? 0 : GetSystemMetricsForDpi(SmallIconWidthMetric, dpi);
        return size > 0 ? size : SystemInformation.SmallIconSize.Width;
    }
}
