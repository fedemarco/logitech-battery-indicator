using LogitechBattery.Battery;
using LogitechBattery.Tray;

namespace LogitechBattery.App.Tests;

public sealed class ShowInTrayMenuTests
{
    private static readonly DeviceId MouseId = DeviceId.Parse("0000000A");
    private static readonly DeviceId KeyboardId = DeviceId.Parse("0000000B");

    private readonly List<DeviceId> _chosen = [];

    [Fact]
    public void Is_hidden_with_one_device() => StaThread.Run(IsHiddenWithOneDevice);

    private void IsHiddenWithOneDevice()
    {
        var menu = new ShowInTrayMenu(Choose);

        menu.Update([Mouse("Mouse 80%", isTray: true)]);

        Assert.False(menu.Item.Available);
    }

    // WinForms draws the submenu arrow exactly when HasDropDownItems is true, so this is the arrow regression check.
    [Fact]
    public void Is_visible_with_two_devices_and_has_its_entries_before_any_open() =>
        StaThread.Run(IsVisibleWithTwoDevicesAndHasItsEntriesBeforeAnyOpen);

    private void IsVisibleWithTwoDevicesAndHasItsEntriesBeforeAnyOpen()
    {
        var menu = new ShowInTrayMenu(Choose);

        menu.Update(MouseAndKeyboard(trayIsMouse: true));

        Assert.True(menu.Item.Available);
        Assert.True(menu.Item.HasDropDownItems);
        Assert.Equal(["Mouse 80%", "Keyboard 60%"], menu.Item.DropDownItems.Labels());
    }

    [Fact]
    public void The_same_devices_again_update_the_same_entries_in_place() =>
        StaThread.Run(TheSameDevicesAgainUpdateTheSameEntriesInPlace);

    private void TheSameDevicesAgainUpdateTheSameEntriesInPlace()
    {
        var menu = new ShowInTrayMenu(Choose);
        menu.Update(MouseAndKeyboard(trayIsMouse: true));
        var entriesBefore = menu.Item.DropDownItems.Cast<ToolStripItem>().ToArray();

        menu.Update([Mouse("Mouse 79%", isTray: false), Keyboard("Keyboard 59%", isTray: true)]);

        Assert.Equal(entriesBefore, menu.Item.DropDownItems.Cast<ToolStripItem>());
        Assert.Equal(["Mouse 79%", "Keyboard 59%"], menu.Item.DropDownItems.Labels());
        Assert.False(CheckedOf(menu, "Mouse 79%"));
        Assert.True(CheckedOf(menu, "Keyboard 59%"));
    }

    [Fact]
    public void Clicking_an_entry_chooses_its_device_and_moves_the_mark() => StaThread.Run(ClickingAnEntryChoosesItsDeviceAndMovesTheMark);

    private void ClickingAnEntryChoosesItsDeviceAndMovesTheMark()
    {
        var menu = new ShowInTrayMenu(Choose);
        menu.Update(MouseAndKeyboard(trayIsMouse: true));

        menu.Item.DropDownItems.ItemNamed("Keyboard 60%").PerformClick();

        Assert.Equal([KeyboardId], _chosen);
        Assert.False(CheckedOf(menu, "Mouse 80%"));
        Assert.True(CheckedOf(menu, "Keyboard 60%"));
    }

    [Fact]
    public void Clicking_an_entry_after_another_update_still_chooses_its_device() =>
        StaThread.Run(ClickingAnEntryAfterAnotherUpdateStillChoosesItsDevice);

    private void ClickingAnEntryAfterAnotherUpdateStillChoosesItsDevice()
    {
        var menu = new ShowInTrayMenu(Choose);
        menu.Update(MouseAndKeyboard(trayIsMouse: true));
        menu.Update([Mouse("Mouse 79%", isTray: false), Keyboard("Keyboard 59%", isTray: true)]);

        menu.Item.DropDownItems.ItemNamed("Mouse 79%").PerformClick();

        Assert.Equal([MouseId], _chosen);
        Assert.True(CheckedOf(menu, "Mouse 79%"));
        Assert.False(CheckedOf(menu, "Keyboard 59%"));
    }

    [Fact]
    public void Clicking_an_entry_in_a_shown_menu_keeps_the_menu_open() => StaThread.Run(ClickingAnEntryInAShownMenuKeepsTheMenuOpen);

    private void ClickingAnEntryInAShownMenuKeepsTheMenuOpen()
    {
        using var host = new ContextMenuStrip();
        var menu = new ShowInTrayMenu(Choose);
        host.Items.Add(menu.Item);
        MenuStaysOpenForToggles.Attach(host);
        menu.Update(MouseAndKeyboard(trayIsMouse: true));
        host.Show(MenuLookup.ShownMenuLocation);
        menu.Item.ShowDropDown();

        menu.Item.DropDownItems.ItemNamed("Keyboard 60%").PerformClick();

        Assert.Equal([KeyboardId], _chosen);
        Assert.True(menu.Item.DropDown.Visible);
        Assert.True(host.Visible);
    }

    private static TrayDeviceItem Mouse(string text, bool isTray) => new(MouseId, text, isTray);

    private static TrayDeviceItem Keyboard(string text, bool isTray) => new(KeyboardId, text, isTray);

    private static TrayDeviceItem[] MouseAndKeyboard(bool trayIsMouse) =>
        [Mouse("Mouse 80%", isTray: trayIsMouse), Keyboard("Keyboard 60%", isTray: !trayIsMouse)];

    private static bool CheckedOf(ShowInTrayMenu menu, string text) => menu.Item.DropDownItems.ItemNamed(text).Checked;

    private void Choose(DeviceId id) => _chosen.Add(id);
}
