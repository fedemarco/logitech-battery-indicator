using LogitechBattery.Battery;

namespace LogitechBattery.Tests;

public class TrayIndicatorTests
{
    private static BatteryStatus Make(ConnectionState c, int? p, ChargingState ch) => new(c, p, ch, DateTimeOffset.UnixEpoch);

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
    public void Discharging_buckets(int percent, string icon) =>
        Assert.Equal(icon, TrayIndicator.From(Make(ConnectionState.Connected, percent, ChargingState.Discharging)).Icon);

    [Theory]
    [InlineData(0, "battery0_chrg")]
    [InlineData(39, "battery25_chrg")]
    [InlineData(60, "battery75_chrg")]
    [InlineData(100, "battery100_chrg")]
    public void Charging_uses_chrg_variant(int percent, string icon) =>
        Assert.Equal(icon, TrayIndicator.From(Make(ConnectionState.Connected, percent, ChargingState.Charging)).Icon);

    [Fact]
    public void Full_at_100_is_plain_full_battery() =>
        Assert.Equal(new TrayIndicator("battery100", IndicatorTint.Green),
            TrayIndicator.From(Make(ConnectionState.Connected, 100, ChargingState.Full)));

    [Fact]
    public void Unavailable_is_unknown_icon()
    {
        var unknown = new TrayIndicator("battery_unknown", IndicatorTint.None);
        Assert.Equal(unknown, TrayIndicator.From(null));
        Assert.Equal(unknown, TrayIndicator.From(Make(ConnectionState.Asleep, 50, ChargingState.Charging)));
        Assert.Equal(unknown, TrayIndicator.From(Make(ConnectionState.NotFound, 50, ChargingState.Discharging)));
        Assert.Equal(unknown, TrayIndicator.From(Make(ConnectionState.Connected, null, ChargingState.Charging)));
    }

    [Theory]
    [InlineData(0, IndicatorTint.Red)]
    [InlineData(14, IndicatorTint.Red)]
    [InlineData(15, IndicatorTint.Orange)]
    [InlineData(49, IndicatorTint.Orange)]
    [InlineData(50, IndicatorTint.Green)]
    public void Tint_follows_level(int percent, IndicatorTint tint)
    {
        Assert.Equal(tint, TrayIndicator.From(Make(ConnectionState.Connected, percent, ChargingState.Discharging)).Tint);
        Assert.Equal(tint, TrayIndicator.From(Make(ConnectionState.Connected, percent, ChargingState.Charging)).Tint);
    }
}
