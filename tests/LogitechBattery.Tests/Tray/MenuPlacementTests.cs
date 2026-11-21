using System.Drawing;
using LogitechBattery.Tray;

namespace LogitechBattery.Tests;

public sealed class MenuPlacementTests
{
    private static readonly Rectangle WorkingArea = new(0, 0, 1920, 1032);
    private static readonly Rectangle MenuNearTheBottom = new(1600, 700, 200, 300);
    private static readonly Rectangle MenuNearTheTop = new(1600, 40, 200, 300);

    [Fact]
    public void A_menu_near_the_bottom_that_grows_keeps_its_bottom_edge()
    {
        var taller = new Size(200, 340);

        var location = MenuPlacement.LocationAfterResize(MenuNearTheBottom, taller, WorkingArea);

        Assert.Equal(new Point(1600, 660), location);
    }

    [Fact]
    public void A_menu_near_the_top_that_grows_keeps_its_top_edge()
    {
        var taller = new Size(200, 340);

        var location = MenuPlacement.LocationAfterResize(MenuNearTheTop, taller, WorkingArea);

        Assert.Equal(new Point(1600, 40), location);
    }

    [Fact]
    public void A_menu_near_the_bottom_that_shrinks_keeps_its_bottom_edge()
    {
        var shorter = new Size(200, 260);

        var location = MenuPlacement.LocationAfterResize(MenuNearTheBottom, shorter, WorkingArea);

        Assert.Equal(new Point(1600, 740), location);
    }

    [Fact]
    public void A_menu_never_grows_above_the_working_area()
    {
        var tallerThanTheScreen = new Size(200, 1100);

        var location = MenuPlacement.LocationAfterResize(MenuNearTheBottom, tallerThanTheScreen, WorkingArea);

        Assert.Equal(new Point(1600, WorkingArea.Top), location);
    }

    [Fact]
    public void A_menu_with_equal_space_above_and_below_keeps_its_top_edge()
    {
        var centered = new Rectangle(1600, 366, 200, 300);
        var taller = new Size(200, 340);

        var location = MenuPlacement.LocationAfterResize(centered, taller, WorkingArea);

        Assert.Equal(new Point(1600, 366), location);
    }

    [Fact]
    public void A_menu_on_a_monitor_with_an_offset_origin_keeps_its_bottom_edge()
    {
        var secondMonitorAboveTheFirst = new Rectangle(1920, -200, 1920, 1032);
        var menuNearItsBottom = new Rectangle(3500, 500, 200, 300);
        var taller = new Size(200, 340);

        var location = MenuPlacement.LocationAfterResize(menuNearItsBottom, taller, secondMonitorAboveTheFirst);

        Assert.Equal(new Point(3500, 460), location);
    }
}
