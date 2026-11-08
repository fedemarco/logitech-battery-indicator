using LogitechBattery.Battery;

namespace LogitechBattery.Tests;

public class TrayIndicatorTests
{
    [Theory]
    [InlineData(-5, "battery0")]
    [InlineData(0, "battery0")]
    [InlineData(19, "battery0")]
    [InlineData(20, "battery25")]
    [InlineData(39, "battery25")]
    [InlineData(40, "battery50")]
    [InlineData(59, "battery50")]
    [InlineData(60, "battery75")]
    [InlineData(79, "battery75")]
    [InlineData(80, "battery100")]
    [InlineData(100, "battery100")]
    [InlineData(150, "battery100")]
    public void Discharging_percent_picks_the_gauge_icon_in_steps_of_25(int percent, string icon) =>
        Assert.Equal(icon, TrayIndicator.From(TestData.Status(percent)).Icon);

    [Theory]
    [InlineData(0, "battery0_chrg")]
    [InlineData(39, "battery25_chrg")]
    [InlineData(60, "battery75_chrg")]
    [InlineData(100, "battery100_chrg")]
    public void Charging_uses_the_chrg_icon_of_the_same_step(int percent, string icon) =>
        Assert.Equal(icon, TrayIndicator.From(TestData.Status(percent, ChargingState.Charging)).Icon);

    [Fact]
    public void Full_at_100_percent_uses_the_plain_full_icon() =>
        Assert.Equal(new TrayIndicator("battery100"),
            TrayIndicator.From(TestData.Status(100, ChargingState.Full)));

    [Fact]
    public void Unavailable_battery_uses_the_unknown_icon()
    {
        var unknown = new TrayIndicator("battery_unknown");
        Assert.Equal(unknown, TrayIndicator.From(null));
        Assert.Equal(unknown, TrayIndicator.From(TestData.Status(50, ChargingState.Charging, ConnectionState.Asleep)));
        Assert.Equal(unknown, TrayIndicator.From(TestData.Status(50, connection: ConnectionState.NotFound)));
        Assert.Equal(unknown, TrayIndicator.From(TestData.Status(null, ChargingState.Charging)));
    }
}
