using LogitechBattery.Battery;
using LogitechBattery.HidPp;

namespace LogitechBattery.Tests;

public class UnifiedBatteryTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Receiver_Sample()
    {
        var s = UnifiedBattery.ParseStatus([0x19, 0x02, 0x00, 0x00], Now);
        Assert.Equal(new BatteryStatus(ConnectionState.Connected, 25, ChargingState.Discharging, Now), s);
    }

    [Fact]
    public void Cable_Sample()
    {
        var s = UnifiedBattery.ParseStatus([0x19, 0x02, 0x01, 0x01], Now);
        Assert.Equal(25, s.Percent);
        Assert.Equal(ChargingState.Charging, s.Charging);
    }

    [Theory]
    [InlineData(0, ChargingState.Discharging)]
    [InlineData(1, ChargingState.Charging)]
    [InlineData(2, ChargingState.Charging)]
    [InlineData(3, ChargingState.Full)]
    [InlineData(4, ChargingState.Unknown)]
    public void ChargingCodes(byte code, ChargingState expected) =>
        Assert.Equal(expected, UnifiedBattery.ParseStatus([50, 4, code, 0], Now).Charging);

    [Fact]
    public void Percent_IsClamped() =>
        Assert.Equal(100, UnifiedBattery.ParseStatus([200, 8, 0, 0], Now).Percent);
}
