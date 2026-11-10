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
    public void Unknown_alert_is_rejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => BatteryAlertText.For((BatteryAlert)99, 50, "PRO X2"));
}
