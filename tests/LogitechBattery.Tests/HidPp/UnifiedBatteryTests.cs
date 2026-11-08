using LogitechBattery.Battery;
using LogitechBattery.HidPp;

namespace LogitechBattery.Tests;

public class UnifiedBatteryTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static byte[] Params(byte percent, byte levelFlags, byte chargingStatus, byte externalPower = 0) =>
        [percent, levelFlags, chargingStatus, externalPower];

    [Fact]
    public void Receiver_sample_is_connected_and_discharging_at_25_percent()
    {
        var status = UnifiedBattery.ParseStatus(Params(0x19, 0x02, 0x00), Now);

        Assert.Equal(new BatteryStatus(ConnectionState.Connected, 25, ChargingState.Discharging, Now), status);
    }

    [Fact]
    public void Cable_sample_is_charging_at_25_percent()
    {
        var status = UnifiedBattery.ParseStatus(Params(0x19, 0x02, 0x01, externalPower: 1), Now);

        Assert.Equal(25, status.Percent);
        Assert.Equal(ChargingState.Charging, status.Charging);
    }

    [Theory]
    [InlineData(0, ChargingState.Discharging)]
    [InlineData(1, ChargingState.Charging)]
    [InlineData(2, ChargingState.Charging)]
    [InlineData(3, ChargingState.Full)]
    [InlineData(4, ChargingState.Unknown)]
    public void Charging_status_code_maps_to_charging_state(byte code, ChargingState expected) =>
        Assert.Equal(expected, UnifiedBattery.ParseStatus(Params(50, 4, code), Now).Charging);

    [Fact]
    public void Percent_above_100_is_clamped_to_100() =>
        Assert.Equal(100, UnifiedBattery.ParseStatus(Params(200, 8, 0), Now).Percent);

    [Fact]
    public void Params_shorter_than_four_bytes_are_rejected() =>
        Assert.Throws<FormatException>(() => UnifiedBattery.ParseStatus([50, 4, 0], Now));
}
