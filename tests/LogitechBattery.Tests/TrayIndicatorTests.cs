using LogitechBattery.Battery;

namespace LogitechBattery.Tests;

public class TrayIndicatorTests
{
    private static BatteryStatus Make(ConnectionState c, int? p, ChargingState ch) => new(c, p, ch, DateTimeOffset.UnixEpoch);

    [Theory]
    [InlineData(0, "0", IndicatorColor.Red)]
    [InlineData(14, "14", IndicatorColor.Red)]
    [InlineData(15, "15", IndicatorColor.Orange)]
    [InlineData(49, "49", IndicatorColor.Orange)]
    [InlineData(50, "50", IndicatorColor.Green)]
    [InlineData(99, "99", IndicatorColor.Green)]
    [InlineData(100, "100", IndicatorColor.Green)]
    public void Text_and_color_boundaries(int percent, string text, IndicatorColor color) =>
        Assert.Equal(new TrayIndicator(text, color, false),
            TrayIndicator.From(Make(ConnectionState.Connected, percent, ChargingState.Discharging)));

    [Theory]
    [InlineData(ChargingState.Charging, true)]
    [InlineData(ChargingState.Full, false)]
    [InlineData(ChargingState.Discharging, false)]
    public void Bolt_only_when_charging(ChargingState state, bool bolt) =>
        Assert.Equal(bolt, TrayIndicator.From(Make(ConnectionState.Connected, 60, state)).Charging);

    [Fact]
    public void Unavailable_is_gray_dash()
    {
        var gray = new TrayIndicator("–", IndicatorColor.Gray, false);
        Assert.Equal(gray, TrayIndicator.From(null));
        Assert.Equal(gray, TrayIndicator.From(Make(ConnectionState.Asleep, 50, ChargingState.Charging)));
        Assert.Equal(gray, TrayIndicator.From(Make(ConnectionState.NotFound, 50, ChargingState.Discharging)));
        Assert.Equal(gray, TrayIndicator.From(Make(ConnectionState.Connected, null, ChargingState.Charging)));
    }
}
