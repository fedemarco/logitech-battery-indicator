using System.Drawing;

namespace LogitechBattery.Tests;

public sealed class OverlayPlacementTests
{
    private static readonly Rectangle Screen = new(0, 0, 1920, 1080);
    private static readonly Rectangle AboveVisibleTaskbar = new(0, 0, 1920, 1032);
    private static readonly Size Panel = new(360, 260);

    [Fact]
    public void Panel_opens_in_the_bottom_right_corner_above_a_visible_taskbar()
    {
        var location = OverlayPlacement.Location(Screen, AboveVisibleTaskbar, Panel, dpiScale: 1f);

        Assert.Equal(new Point(1920 - 360 - 8, 1032 - 260 - 8), location);
    }

    [Fact]
    public void Panel_opens_above_the_taskbar_when_the_taskbar_auto_hides()
    {
        var location = OverlayPlacement.Location(Screen, workingArea: Screen, Panel, dpiScale: 1f);

        Assert.Equal(new Point(1920 - 360 - 8, 1080 - 48 - 260 - 8), location);
    }

    [Fact]
    public void Margin_and_taskbar_lift_scale_with_the_display_scaling()
    {
        var panelAt150 = new Size(540, 390);

        var location = OverlayPlacement.Location(Screen, workingArea: Screen, panelAt150, dpiScale: 1.5f);

        Assert.Equal(new Point(1920 - 540 - 12, 1080 - 72 - 390 - 12), location);
    }
}
