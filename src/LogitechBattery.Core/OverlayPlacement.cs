using System.Drawing;

namespace LogitechBattery;

/// <summary>The screen edge the taskbar is docked to, in the order of the Windows ABE_* values.</summary>
public enum TaskbarEdge { Left, Top, Right, Bottom }

public sealed record TaskbarPosition(Rectangle Bounds, TaskbarEdge Edge);

/// <summary>Where the overlay panel opens: the bottom-right corner of the working area, clear of the taskbar.</summary>
public static class OverlayPlacement
{
    private const int Margin = 8;

    public static Point Location(Rectangle workingArea, TaskbarPosition? taskbar, Size panel, float dpiScale)
    {
        var corner = UncoveredCorner(workingArea, taskbar);
        var margin = (int)(Margin * dpiScale);
        return new Point(corner.X - panel.Width - margin, corner.Y - panel.Height - margin);
    }

    // A visible taskbar is already outside the working area. An auto-hidden one reserves no space and slides over the
    // working area, so the panel must keep clear of it. A taskbar on another monitor does not overlap this working area.
    private static Point UncoveredCorner(Rectangle workingArea, TaskbarPosition? taskbar)
    {
        var corner = new Point(workingArea.Right, workingArea.Bottom);
        if (taskbar is null || !taskbar.Bounds.IntersectsWith(workingArea))
            return corner;

        if (taskbar.Edge == TaskbarEdge.Bottom)
            corner.Y = Math.Min(corner.Y, taskbar.Bounds.Top);
        if (taskbar.Edge == TaskbarEdge.Right)
            corner.X = Math.Min(corner.X, taskbar.Bounds.Left);
        return corner;
    }
}
