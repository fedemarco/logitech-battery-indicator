using System.Runtime.InteropServices;

namespace LogitechBattery;

/// <summary>Asks Windows where the taskbar is. The answer includes an auto-hidden taskbar, which the working area ignores.</summary>
internal static class Taskbar
{
    private const uint GetTaskbarPosition = 0x5; // ABM_GETTASKBARPOS

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AppBarData // APPBARDATA
    {
        public uint StructSize; // cbSize: Windows checks it against the struct version it expects
        public IntPtr Window;
        public uint CallbackMessage;
        public uint Edge;
        public NativeRect Bounds;
        public IntPtr Param;
    }

    [DllImport("shell32.dll")]
    private static extern UIntPtr SHAppBarMessage(uint message, ref AppBarData data);

    /// <returns>null when Windows reports no taskbar, for example while Explorer restarts.</returns>
    public static TaskbarPosition? Find()
    {
        // shortcut: Windows reports only the main taskbar, so an auto-hidden taskbar on another monitor is not avoided;
        // enumerate the Shell_SecondaryTrayWnd windows if the panel opens on such a monitor.
        var data = new AppBarData { StructSize = (uint)Marshal.SizeOf<AppBarData>() };
        if (SHAppBarMessage(GetTaskbarPosition, ref data) == UIntPtr.Zero)
            return null;
        if (!Enum.IsDefined((TaskbarEdge)data.Edge)) // a value from the OS: never trust it to be one of the four edges
            return null;

        var bounds = data.Bounds;
        return new TaskbarPosition(Rectangle.FromLTRB(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom), (TaskbarEdge)data.Edge);
    }
}
