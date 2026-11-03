using LogitechBattery.Battery;

namespace LogitechBattery.Tests;

public class MockBatteryProviderTests
{
    private sealed class FakeTime : TimeProvider
    {
        public double Seconds;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => (long)(Seconds * 1000);
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddSeconds(Seconds);
    }

    // Auto cycle: discharge 96 s, asleep 5 s, charge 95/3 s, full 10 s.
    private const double Cycle = 96 + 5 + 95.0 / 3 + 10;

    private static BatteryStatus Read(MockBatteryProvider p) => p.ReadAsync().Result;

    [Theory]
    [InlineData(0, ConnectionState.Connected, 100, ChargingState.Discharging)]
    [InlineData(95.5, ConnectionState.Connected, 5, ChargingState.Discharging)]
    [InlineData(96, ConnectionState.Asleep, null, ChargingState.Unknown)]
    [InlineData(100.9, ConnectionState.Asleep, null, ChargingState.Unknown)]
    [InlineData(101, ConnectionState.Connected, 5, ChargingState.Charging)]
    [InlineData(101 + 95.0 / 3 - 0.1, ConnectionState.Connected, 99, ChargingState.Charging)]
    [InlineData(101 + 95.0 / 3 + 0.1, ConnectionState.Connected, 100, ChargingState.Full)]
    [InlineData(Cycle + 0.1, ConnectionState.Connected, 100, ChargingState.Discharging)]
    public void Auto_cycle(double t, ConnectionState c, int? percent, ChargingState ch)
    {
        var time = new FakeTime();
        var p = new MockBatteryProvider(time);
        time.Seconds = t;
        var s = Read(p);
        Assert.Equal((c, percent, ch), (s.Connection, s.Percent, s.Charging));
    }

    [Fact]
    public void Setting_auto_restarts_cycle()
    {
        var time = new FakeTime();
        var p = new MockBatteryProvider(time);
        time.Seconds = 50;
        Assert.Equal(50, Read(p).Percent);
        p.Mode = MockMode.Auto;
        Assert.Equal(100, Read(p).Percent);
    }

    [Fact]
    public void Auto_percent_stays_in_range()
    {
        var time = new FakeTime();
        var p = new MockBatteryProvider(time);
        for (double t = 0; t < Cycle; t += 0.25)
        {
            time.Seconds = t;
            var percent = Read(p).Percent;
            Assert.True(percent is null or (>= 5 and <= 100), $"t={t}: {percent}");
        }
    }

    [Theory]
    [InlineData(MockMode.Discharging85, ConnectionState.Connected, 85, ChargingState.Discharging)]
    [InlineData(MockMode.Discharging25, ConnectionState.Connected, 25, ChargingState.Discharging)]
    [InlineData(MockMode.Discharging10, ConnectionState.Connected, 10, ChargingState.Discharging)]
    [InlineData(MockMode.Charging60, ConnectionState.Connected, 60, ChargingState.Charging)]
    [InlineData(MockMode.Full100, ConnectionState.Connected, 100, ChargingState.Full)]
    [InlineData(MockMode.Asleep, ConnectionState.Asleep, null, ChargingState.Unknown)]
    [InlineData(MockMode.NotFound, ConnectionState.NotFound, null, ChargingState.Unknown)]
    public void Presets(MockMode mode, ConnectionState c, int? percent, ChargingState ch)
    {
        var p = new MockBatteryProvider(new FakeTime()) { Mode = mode };
        var s = Read(p);
        Assert.Equal((c, percent, ch), (s.Connection, s.Percent, s.Charging));
    }
}
