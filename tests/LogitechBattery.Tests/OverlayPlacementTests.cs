using System.Drawing;

namespace LogitechBattery.Tests;

public sealed class OverlayPlacementTests
{
    private static readonly Rectangle WholeScreen = new(0, 0, 1920, 1080);
    private static readonly Rectangle AboveBottomTaskbar = new(0, 0, 1920, 1032);
    private static readonly TaskbarPosition BottomTaskbar = new(new Rectangle(0, 1032, 1920, 48), TaskbarEdge.Bottom);
    private static readonly Size Panel = new(360, 260);

    private static Point PanelLocation(Rectangle workingArea, TaskbarPosition? taskbar, float dpiScale = 1f) =>
        OverlayPlacement.Location(workingArea, taskbar, Panel, dpiScale);

    [Fact]
    public void Panel_opens_in_the_bottom_right_corner_above_a_visible_taskbar()
    {
        Assert.Equal(new Point(1920 - 360 - 8, 1032 - 260 - 8), PanelLocation(AboveBottomTaskbar, BottomTaskbar));
    }

    [Fact]
    public void Panel_opens_above_an_auto_hidden_bottom_taskbar()
    {
        var workingAreaWithAutoHide = WholeScreen; // an auto-hidden taskbar reserves no space

        Assert.Equal(new Point(1920 - 360 - 8, 1032 - 260 - 8), PanelLocation(workingAreaWithAutoHide, BottomTaskbar));
    }

    [Fact]
    public void Panel_opens_left_of_an_auto_hidden_right_taskbar()
    {
        var rightTaskbar = new TaskbarPosition(new Rectangle(1872, 0, 48, 1080), TaskbarEdge.Right);

        Assert.Equal(new Point(1872 - 360 - 8, 1080 - 260 - 8), PanelLocation(WholeScreen, rightTaskbar));
    }

    [Fact]
    public void A_taskbar_on_the_top_edge_does_not_move_the_panel()
    {
        var topTaskbar = new TaskbarPosition(new Rectangle(0, 0, 1920, 48), TaskbarEdge.Top);

        Assert.Equal(new Point(1920 - 360 - 8, 1080 - 260 - 8), PanelLocation(WholeScreen, topTaskbar));
    }

    [Fact]
    public void A_taskbar_on_another_monitor_does_not_move_the_panel()
    {
        var secondMonitor = new Rectangle(1920, 0, 1920, 1080);

        Assert.Equal(new Point(3840 - 360 - 8, 1080 - 260 - 8), PanelLocation(secondMonitor, BottomTaskbar));
    }

    [Fact]
    public void Without_a_taskbar_the_panel_uses_the_working_area_corner()
    {
        Assert.Equal(new Point(1920 - 360 - 8, 1080 - 260 - 8), PanelLocation(WholeScreen, taskbar: null));
    }

    [Fact]
    public void The_margin_scales_with_the_display_scaling()
    {
        Assert.Equal(new Point(1920 - 360 - 12, 1032 - 260 - 12), PanelLocation(AboveBottomTaskbar, BottomTaskbar, dpiScale: 1.5f));
    }
}
