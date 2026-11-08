using LogitechBattery.Battery;

namespace LogitechBattery.Tests;

public class BatteryAlertTextTests
{
    [Fact]
    public void A_critical_alert_names_the_level() =>
        Assert.Equal(("Mouse battery critical", "5% left."), BatteryAlertText.For(BatteryAlert.Critical, 5));

    [Fact]
    public void A_low_alert_asks_to_charge_soon() =>
        Assert.Equal(("Mouse battery low", "15% left. Charge it soon."), BatteryAlertText.For(BatteryAlert.Low, 15));

    [Fact]
    public void A_full_alert_says_the_mouse_is_charged() =>
        Assert.Equal(("Mouse fully charged", "100%."), BatteryAlertText.For(BatteryAlert.Full, 100));

    [Fact]
    public void An_undefined_alert_is_rejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => BatteryAlertText.For((BatteryAlert)99, 50));
}
