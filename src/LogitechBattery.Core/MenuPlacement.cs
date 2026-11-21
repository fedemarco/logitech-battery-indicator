using System.Drawing;

namespace LogitechBattery;

/// <summary>Where an open menu goes after it changes size: it keeps the edge nearest the screen edge, so a menu above a bottom taskbar does not grow over it.</summary>
public static class MenuPlacement
{
    public static Point LocationAfterResize(Rectangle boundsBefore, Size sizeAfter, Rectangle workingArea)
    {
        var spaceBelow = workingArea.Bottom - boundsBefore.Bottom;
        var spaceAbove = boundsBefore.Top - workingArea.Top;
        if (spaceBelow >= spaceAbove)
            return boundsBefore.Location;

        var topKeepingBottomEdge = boundsBefore.Bottom - sizeAfter.Height;
        return new Point(boundsBefore.X, Math.Max(topKeepingBottomEdge, workingArea.Top));
    }
}
