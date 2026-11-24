using LogitechBattery.Alerts;
using LogitechBattery.Battery;

namespace LogitechBattery.Tests;

public class BatteryAlertTextTests
{
    [Fact]
    public void Critical_title_names_the_device() =>
        Assert.Equal(("PRO X2 battery critical", "5% left."), BatteryAlertText.For(BatteryAlert.Critical, 5, "PRO X2"));

    [Fact]
    public void Low_title_names_the_device() =>
        Assert.Equal(("PRO X2 battery low", "15% left. Charge it soon."), BatteryAlertText.For(BatteryAlert.Low, 15, "PRO X2"));

    [Fact]
    public void Full_title_names_the_device() =>
        Assert.Equal(("PRO X2 fully charged", "100%."), BatteryAlertText.For(BatteryAlert.Full, 100, "PRO X2"));

    [Fact]
    public void One_alert_in_a_read_keeps_its_own_title_and_text()
    {
        var balloon = BatteryAlertText.ForRead([DeviceAlertFor("PRO X2", BatteryAlert.Low, 15)]);

        Assert.Equal(new BalloonContent("PRO X2 battery low", "15% left. Charge it soon.", IsWarning: true), balloon);
    }

    [Fact]
    public void One_full_alert_in_a_read_is_info()
    {
        var balloon = BatteryAlertText.ForRead([DeviceAlertFor("PRO X2", BatteryAlert.Full, 100)]);

        Assert.Equal(new BalloonContent("PRO X2 fully charged", "100%.", IsWarning: false), balloon);
    }

    [Fact]
    public void Two_low_alerts_make_one_battery_low_balloon_with_a_line_each_in_read_order()
    {
        var balloon = BatteryAlertText.ForRead([DeviceAlertFor("Mouse", BatteryAlert.Low, 15), DeviceAlertFor("Keyboard", BatteryAlert.Low, 14)]);

        Assert.Equal(new BalloonContent("Battery low", "Mouse: 15% left\nKeyboard: 14% left", IsWarning: true), balloon);
    }

    [Fact]
    public void A_full_alert_comes_after_a_low_alert_and_the_balloon_warns()
    {
        var balloon = BatteryAlertText.ForRead([DeviceAlertFor("Keyboard", BatteryAlert.Full, 100), DeviceAlertFor("Mouse", BatteryAlert.Low, 15)]);

        Assert.Equal(new BalloonContent("Battery low", "Mouse: 15% left\nKeyboard: fully charged", IsWarning: true), balloon);
    }

    [Fact]
    public void Two_full_alerts_make_one_fully_charged_info_balloon()
    {
        var balloon = BatteryAlertText.ForRead([DeviceAlertFor("Mouse", BatteryAlert.Full, 100), DeviceAlertFor("Keyboard", BatteryAlert.Full, 100)]);

        Assert.Equal(new BalloonContent("Fully charged", "Mouse: fully charged\nKeyboard: fully charged", IsWarning: false), balloon);
    }

    [Fact]
    public void A_critical_alert_comes_first_and_sets_the_title()
    {
        var balloon = BatteryAlertText.ForRead([DeviceAlertFor("Mouse", BatteryAlert.Low, 15), DeviceAlertFor("Keyboard", BatteryAlert.Critical, 5)]);

        Assert.Equal(new BalloonContent("Battery critical", "Keyboard: 5% left\nMouse: 15% left", IsWarning: true), balloon);
    }

    [Fact]
    public void A_full_alert_goes_after_a_critical_alert_and_the_balloon_warns()
    {
        var balloon = BatteryAlertText.ForRead([DeviceAlertFor("Mouse", BatteryAlert.Full, 100), DeviceAlertFor("Keyboard", BatteryAlert.Critical, 5)]);

        Assert.Equal(new BalloonContent("Battery critical", "Keyboard: 5% left\nMouse: fully charged", IsWarning: true), balloon);
    }

    [Fact]
    public void A_read_without_alerts_has_no_balloon() =>
        Assert.Throws<ArgumentException>(() => BatteryAlertText.ForRead([]));

    private static DeviceAlert DeviceAlertFor(string deviceName, BatteryAlert alert, int percent) => new(deviceName, alert, percent);
}
