using LogitechBattery.Battery;
using LogitechBattery.History;

namespace LogitechBattery.App.Tests;

public sealed class TrayMenuTests
{
    private int _refreshCount;
    private int _exitCount;

    [Fact]
    public void Real_provider_menu_lists_the_entries_in_order() => StaThread.Run(RealProviderMenuListsTheEntriesInOrder);

    private void RealProviderMenuListsTheEntriesInOrder()
    {
        // Never Show this menu or click Start with Windows here: Opening reads the real registry and the click writes it.
        var histories = new DeviceHistories(Path.Combine(Path.GetTempPath(), $"logitech-battery-tests-{Guid.NewGuid():N}")); // never created: the history is only touched on use
        using var menu = BuildMenu(new NoDevicesBatteryProvider(), histories);

        Assert.Equal(
            ["Show in tray", "Notifications", "-", "Check for updates", "Start with Windows", "Clear history…", "Exit"],
            menu.Items.Labels());
    }

    [Fact]
    public void Simulator_menu_starts_with_the_simulator_and_has_no_start_or_history_entry() =>
        StaThread.Run(SimulatorMenuStartsWithTheSimulatorAndHasNoStartOrHistoryEntry);

    private void SimulatorMenuStartsWithTheSimulatorAndHasNoStartOrHistoryEntry()
    {
        using var menu = BuildMenu(new MockBatteryProvider(), histories: null);

        Assert.Equal(["Simulator", "Show in tray", "Notifications", "-", "Check for updates", "Exit"], menu.Items.Labels());
    }

    [Fact]
    public void Choosing_a_simulator_mode_keeps_the_menu_open_and_moves_the_mark() =>
        StaThread.Run(ChoosingASimulatorModeKeepsTheMenuOpenAndMovesTheMark);

    private void ChoosingASimulatorModeKeepsTheMenuOpenAndMovesTheMark()
    {
        var mock = new MockBatteryProvider();
        using var menu = BuildMenu(mock, histories: null);
        var simulator = ShowSimulatorSubmenu(menu);

        simulator.DropDownItems.ItemNamed("Asleep").PerformClick();

        Assert.Equal(MockMode.Asleep, mock.Mode);
        Assert.True(simulator.DropDownItems.ItemNamed("Asleep").Checked);
        Assert.False(simulator.DropDownItems.ItemNamed("Auto cycle").Checked);
        Assert.Equal(1, _refreshCount);
        Assert.True(menu.Visible);
    }

    [Fact]
    public void Switching_Several_devices_keeps_the_menu_open_and_moves_the_mark() =>
        StaThread.Run(SwitchingSeveralDevicesKeepsTheMenuOpenAndMovesTheMark);

    private void SwitchingSeveralDevicesKeepsTheMenuOpenAndMovesTheMark()
    {
        var mock = new MockBatteryProvider();
        using var menu = BuildMenu(mock, histories: null);
        var simulator = ShowSimulatorSubmenu(menu);

        simulator.DropDownItems.ItemNamed("Several devices").PerformClick();

        Assert.True(mock.SeveralDevices);
        Assert.True(simulator.DropDownItems.ItemNamed("Several devices").Checked);
        Assert.Equal(1, _refreshCount);
        Assert.True(menu.Visible);
    }

    [Fact]
    public void An_action_item_closes_the_menu() => StaThread.Run(AnActionItemClosesTheMenu);

    private void AnActionItemClosesTheMenu()
    {
        using var menu = BuildMenu(new MockBatteryProvider(), histories: null);
        menu.Show(MenuLookup.ShownMenuLocation);

        menu.Items.ItemNamed("Check for updates").PerformClick();

        Assert.False(menu.Visible);
    }

    [Fact]
    public void An_action_item_closes_the_menu_after_a_check_mark_click_kept_it_open() =>
        StaThread.Run(AnActionItemClosesTheMenuAfterACheckMarkClickKeptItOpen);

    private void AnActionItemClosesTheMenuAfterACheckMarkClickKeptItOpen()
    {
        using var menu = BuildMenu(new MockBatteryProvider(), histories: null);
        var simulator = ShowSimulatorSubmenu(menu);
        simulator.DropDownItems.ItemNamed("Several devices").PerformClick();
        Assert.True(menu.Visible);

        menu.Items.ItemNamed("Check for updates").PerformClick();

        Assert.False(menu.Visible);
    }

    [Fact]
    public void Exit_closes_the_menu_and_calls_exit_once() => StaThread.Run(ExitClosesTheMenuAndCallsExitOnce);

    private void ExitClosesTheMenuAndCallsExitOnce()
    {
        using var menu = BuildMenu(new MockBatteryProvider(), histories: null);
        menu.Show(MenuLookup.ShownMenuLocation);

        menu.Items.ItemNamed("Exit").PerformClick();

        Assert.Equal(1, _exitCount);
        Assert.False(menu.Visible);
    }

    private ContextMenuStrip BuildMenu(IBatteryProvider provider, DeviceHistories? histories) =>
        TrayMenu.Build(
            provider,
            histories,
            showInTray: new ToolStripMenuItem("Show in tray"),
            notifications: new ToolStripMenuItem("Notifications"),
            checkForUpdates: new ToolStripMenuItem("Check for updates"),
            refreshWithoutWaiting: CountRefresh,
            historyCleared: IgnoreHistoryCleared,
            exit: CountExit);

    private static ToolStripMenuItem ShowSimulatorSubmenu(ContextMenuStrip menu)
    {
        menu.Show(MenuLookup.ShownMenuLocation);
        var simulator = menu.Items.ItemNamed("Simulator");
        simulator.ShowDropDown();
        return simulator;
    }

    private void CountRefresh() => _refreshCount++;

    private void CountExit() => _exitCount++;

    private static void IgnoreHistoryCleared()
    {
    }
}
