using LogitechBattery.Battery;
using LogitechBattery.Menu;
using LogitechBattery.Tray;

namespace LogitechBattery.App.Tests;

public sealed class MenuKeepsScreenEdgeTests
{
    private const int DistanceAboveTheScreenBottom = 40;

    [Fact]
    public void A_menu_open_near_the_bottom_keeps_its_bottom_edge_when_Show_in_tray_appears() =>
        StaThread.Run(AMenuOpenNearTheBottomKeepsItsBottomEdgeWhenShowInTrayAppears);

    private static void AMenuOpenNearTheBottomKeepsItsBottomEdgeWhenShowInTrayAppears()
    {
        using var host = new ContextMenuStrip();
        var showInTray = HostWithShowInTray(host);
        host.Show(PointNearTheScreenBottom(), ToolStripDropDownDirection.AboveRight);
        var bottomBefore = host.Bottom;
        var heightBefore = host.Height;

        showInTray.Update(TwoDevices());

        Assert.True(host.Height > heightBefore);
        Assert.Equal(bottomBefore, host.Bottom);
    }

    [Fact]
    public void A_menu_changed_while_closed_stays_where_it_is_shown() => StaThread.Run(AMenuChangedWhileClosedStaysWhereItIsShown);

    private static void AMenuChangedWhileClosedStaysWhereItIsShown()
    {
        using var host = new ContextMenuStrip();
        var showInTray = HostWithShowInTray(host);
        host.Show(MenuLookup.ShownMenuLocation);
        host.Close();
        showInTray.Update(TwoDevices());
        var shownAt = PointNearTheScreenBottom();

        host.Show(shownAt, ToolStripDropDownDirection.AboveRight);

        Assert.Equal(shownAt.Y, host.Bottom);
    }

    private static ShowInTrayMenu HostWithShowInTray(ContextMenuStrip host)
    {
        var showInTray = new ShowInTrayMenu(IgnoreChoice);
        host.Items.Add(showInTray.Item);
        host.Items.Add(new ToolStripMenuItem("Exit"));
        MenuKeepsScreenEdge.Attach(host);
        return showInTray;
    }

    private static Point PointNearTheScreenBottom()
    {
        var workingArea = Screen.FromPoint(MenuLookup.ShownMenuLocation).WorkingArea;
        return new Point(MenuLookup.ShownMenuLocation.X, workingArea.Bottom - DistanceAboveTheScreenBottom);
    }

    private static TrayDeviceItem[] TwoDevices() => [Device("0000000A", "Mouse 80%"), Device("0000000B", "Keyboard 60%")];

    private static TrayDeviceItem Device(string id, string text) => new(DeviceId.Parse(id), text, IsTray: false);

    private static void IgnoreChoice(DeviceId id)
    {
    }
}
