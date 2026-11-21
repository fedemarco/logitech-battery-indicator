using LogitechBattery.Alerts;
using LogitechBattery.Battery;
using LogitechBattery.Menu;

namespace LogitechBattery.App.Tests;

public sealed class NotificationsMenuTests
{
    private AlertSettings _settings = AlertSettings.Default;
    private bool _checkForUpdates = true;
    private int _applyCount;
    private int _toggleUpdatesCount;

    [Theory]
    [InlineData(10)]
    [InlineData(15)]
    [InlineData(20)]
    public void Choosing_a_low_battery_level_applies_it_once_and_keeps_both_menus_open(int percent) =>
        StaThread.Run(() => ChoosingALowBatteryLevelAppliesItOnceAndKeepsBothMenusOpen(percent));

    private void ChoosingALowBatteryLevelAppliesItOnceAndKeepsBothMenusOpen(int percent)
    {
        using var host = new ContextMenuStrip();
        var notifications = ShowNotifications(host);
        var lowBattery = notifications.DropDownItems.ItemNamed($"Low battery: {AlertSettings.Default.LowPercent}%");
        lowBattery.ShowDropDown();

        lowBattery.DropDownItems.ItemNamed($"{percent}%").PerformClick();

        Assert.Equal(percent, _settings.LowPercent);
        Assert.Equal(1, _applyCount);
        Assert.True(lowBattery.DropDownItems.ItemNamed($"{percent}%").Checked);
        Assert.Equal($"Low battery: {percent}%", lowBattery.Text);
        Assert.True(lowBattery.DropDown.Visible);
        Assert.True(notifications.DropDown.Visible);
        Assert.True(host.Visible);
    }

    [Fact]
    public void Switching_Fully_charged_off_applies_it_once_and_keeps_the_menu_open() =>
        StaThread.Run(SwitchingFullyChargedOffAppliesItOnceAndKeepsTheMenuOpen);

    private void SwitchingFullyChargedOffAppliesItOnceAndKeepsTheMenuOpen()
    {
        using var host = new ContextMenuStrip();
        var notifications = ShowNotifications(host);

        notifications.DropDownItems.ItemNamed("Fully charged").PerformClick();

        Assert.False(_settings.FullChargeAlert);
        Assert.Equal(1, _applyCount);
        Assert.False(notifications.DropDownItems.ItemNamed("Fully charged").Checked);
        Assert.True(notifications.DropDown.Visible);
        Assert.True(host.Visible);
    }

    [Fact]
    public void Switching_New_app_versions_off_toggles_it_once_and_keeps_the_menu_open() =>
        StaThread.Run(SwitchingNewAppVersionsOffTogglesItOnceAndKeepsTheMenuOpen);

    private void SwitchingNewAppVersionsOffTogglesItOnceAndKeepsTheMenuOpen()
    {
        using var host = new ContextMenuStrip();
        var notifications = ShowNotifications(host);

        notifications.DropDownItems.ItemNamed("New app versions").PerformClick();

        Assert.False(_checkForUpdates);
        Assert.Equal(1, _toggleUpdatesCount);
        Assert.False(notifications.DropDownItems.ItemNamed("New app versions").Checked);
        Assert.True(notifications.DropDown.Visible);
        Assert.True(host.Visible);
    }

    private ToolStripMenuItem ShowNotifications(ContextMenuStrip host)
    {
        var notifications = NotificationsMenu.Build(CurrentSettings, ApplySettings, CurrentCheckForUpdates, ToggleCheckForUpdates);
        host.Items.Add(notifications);
        MenuStaysOpenForToggles.Attach(host);
        host.Show(MenuLookup.ShownMenuLocation);
        notifications.ShowDropDown();
        return notifications;
    }

    private AlertSettings CurrentSettings() => _settings;

    private bool CurrentCheckForUpdates() => _checkForUpdates;

    private void ApplySettings(AlertSettings updated)
    {
        _settings = updated;
        _applyCount++;
    }

    private void ToggleCheckForUpdates()
    {
        _checkForUpdates = !_checkForUpdates;
        _toggleUpdatesCount++;
    }
}
