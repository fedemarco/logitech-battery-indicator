using LogitechBattery.Battery;

namespace LogitechBattery.Tests;

public class TrayIndicatorTests
{
    private static BatteryStatus Make(ConnectionState c, int? p, ChargingState ch) => new(c, p, ch, DateTimeOffset.UnixEpoch);

    [Theory]
    [InlineData(0, '\uE850')]
    [InlineData(4, '\uE850')]
    [InlineData(5, '\uE851')]
    [InlineData(94, '\uE859')]
    [InlineData(95, '\uE83F')]
    [InlineData(100, '\uE83F')]
    public void Discharging_glyph_boundaries(int percent, char glyph) =>
        Assert.Equal(glyph, TrayIndicator.From(Make(ConnectionState.Connected, percent, ChargingState.Discharging)).Glyph);

    [Theory]
    [InlineData(0, '\uE85A')]
    [InlineData(60, '\uE860')]
    [InlineData(84, '\uE862')]
    [InlineData(85, '\uE83E')]
    [InlineData(100, '\uE83E')]
    public void Charging_glyph_is_capped_at_level_9(int percent, char glyph) =>
        Assert.Equal(glyph, TrayIndicator.From(Make(ConnectionState.Connected, percent, ChargingState.Charging)).Glyph);

    [Fact]
    public void Full_at_100_is_full_battery() =>
        Assert.Equal(new TrayIndicator('\uE83F', IndicatorTint.Theme),
            TrayIndicator.From(Make(ConnectionState.Connected, 100, ChargingState.Full)));

    [Fact]
    public void Unavailable_is_unknown_glyph()
    {
        var unknown = new TrayIndicator('\uE996', IndicatorTint.Theme);
        Assert.Equal(unknown, TrayIndicator.From(null));
        Assert.Equal(unknown, TrayIndicator.From(Make(ConnectionState.Asleep, 50, ChargingState.Charging)));
        Assert.Equal(unknown, TrayIndicator.From(Make(ConnectionState.NotFound, 50, ChargingState.Discharging)));
        Assert.Equal(unknown, TrayIndicator.From(Make(ConnectionState.Connected, null, ChargingState.Charging)));
    }

    [Theory]
    [InlineData(-5, IndicatorTint.Red)]
    [InlineData(0, IndicatorTint.Red)]
    [InlineData(14, IndicatorTint.Red)]
    [InlineData(15, IndicatorTint.Orange)]
    [InlineData(49, IndicatorTint.Orange)]
    [InlineData(50, IndicatorTint.Theme)]
    [InlineData(150, IndicatorTint.Theme)]
    public void Tint_follows_level(int percent, IndicatorTint tint)
    {
        Assert.Equal(tint, TrayIndicator.From(Make(ConnectionState.Connected, percent, ChargingState.Discharging)).Tint);
        Assert.Equal(tint, TrayIndicator.From(Make(ConnectionState.Connected, percent, ChargingState.Charging)).Tint);
    }

    [Fact]
    public void Clamps_out_of_range_percent()
    {
        Assert.Equal('\uE850', TrayIndicator.From(Make(ConnectionState.Connected, -5, ChargingState.Discharging)).Glyph);
        Assert.Equal('\uE83F', TrayIndicator.From(Make(ConnectionState.Connected, 150, ChargingState.Discharging)).Glyph);
    }
}
