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

    private const double AsleepStart = MockBatteryProvider.DischargeSeconds;
    private const double ChargeStart = AsleepStart + MockBatteryProvider.AsleepSeconds;
    private const double FullStart = ChargeStart + MockBatteryProvider.ChargeSeconds;
    private const double NextCycleStart = MockBatteryProvider.CycleSeconds;
    private const double Epsilon = 0.1;

    private static BatteryStatus Read(MockBatteryProvider provider) => provider.ReadAsync().Result[0].Status!;

    private static BatteryStatus ReadAt(double seconds)
    {
        var time = new FakeTime();
        var provider = new MockBatteryProvider(time);
        time.Seconds = seconds;
        return Read(provider);
    }

    [Theory]
    [InlineData(0, ConnectionState.Connected, 100, ChargingState.Discharging)] // cycle start: full battery
    [InlineData(AsleepStart - 0.5, ConnectionState.Connected, 5, ChargingState.Discharging)] // last second at the 5 % floor
    [InlineData(AsleepStart, ConnectionState.Asleep, null, ChargingState.Unknown)]
    [InlineData(ChargeStart - Epsilon, ConnectionState.Asleep, null, ChargingState.Unknown)]
    [InlineData(ChargeStart, ConnectionState.Connected, 5, ChargingState.Charging)]
    [InlineData(FullStart - Epsilon, ConnectionState.Connected, 99, ChargingState.Charging)] // charging never shows 100
    [InlineData(FullStart + Epsilon, ConnectionState.Connected, 100, ChargingState.Full)]
    [InlineData(NextCycleStart + Epsilon, ConnectionState.Connected, 100, ChargingState.Discharging)] // wraps to the next cycle
    public void Auto_cycle_follows_discharge_asleep_charge_full_phases(double seconds, ConnectionState connection, int? percent, ChargingState charging)
    {
        var status = ReadAt(seconds);

        Assert.Equal((connection, percent, charging), (status.Connection, status.Percent, status.Charging));
    }

    [Fact]
    public void Setting_auto_mode_restarts_the_cycle()
    {
        var time = new FakeTime();
        var provider = new MockBatteryProvider(time);
        time.Seconds = 50;
        Assert.Equal(50, Read(provider).Percent);

        provider.Mode = MockMode.Auto;

        Assert.Equal(100, Read(provider).Percent);
    }

    [Fact]
    public void Auto_percent_stays_between_floor_and_full_throughout_the_cycle()
    {
        for (double seconds = 0; seconds < MockBatteryProvider.CycleSeconds; seconds += 0.25)
        {
            var percent = ReadAt(seconds).Percent;
            Assert.True(percent is null or (>= 5 and <= 100), $"t={seconds}: {percent}");
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
    public void Fixed_mode_reports_its_reading(MockMode mode, ConnectionState connection, int? percent, ChargingState charging)
    {
        var provider = new MockBatteryProvider(new FakeTime()) { Mode = mode };

        var status = Read(provider);

        Assert.Equal((connection, percent, charging), (status.Connection, status.Percent, status.Charging));
    }
}
