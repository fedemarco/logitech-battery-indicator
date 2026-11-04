using LogitechBattery.Battery;

namespace LogitechBattery.Tests;

public class BatteryAlertsTests
{
    private static BatteryStatus Dis(int p) => new(ConnectionState.Connected, p, ChargingState.Discharging, DateTimeOffset.UnixEpoch);
    private static BatteryStatus Chg(int p, ChargingState s = ChargingState.Charging) => new(ConnectionState.Connected, p, s, DateTimeOffset.UnixEpoch);

    private static BatteryAlert?[] Run(BatteryAlerts a, params BatteryStatus[] readings) => readings.Select(a.Update).ToArray();

    [Fact]
    public void Normal_discharge() =>
        Assert.Equal([null, BatteryAlert.Low, null, BatteryAlert.Critical, null],
            Run(new(), Dis(20), Dis(15), Dis(10), Dis(5), Dis(3)));

    [Fact]
    public void Startup_at_4_is_critical_only() =>
        Assert.Equal([BatteryAlert.Critical, null], Run(new(), Dis(4), Dis(3)));

    [Fact]
    public void Startup_at_10_is_low() =>
        Assert.Equal([BatteryAlert.Low, null], Run(new(), Dis(10), Dis(9)));

    [Fact]
    public void Jump_past_both_is_critical_only() =>
        Assert.Equal([null, BatteryAlert.Critical, null], Run(new(), Dis(30), Dis(4), Dis(4)));

    [Fact]
    public void Charging_resets() =>
        Assert.Equal([BatteryAlert.Low, null, BatteryAlert.Low], Run(new(), Dis(10), Chg(10), Dis(10)));

    [Fact]
    public void Full_resets() =>
        Assert.Equal([BatteryAlert.Low, BatteryAlert.Full, BatteryAlert.Low], Run(new(), Dis(10), Chg(100, ChargingState.Full), Dis(10)));

    [Fact]
    public void Bounce_fires_once() =>
        Assert.Equal([BatteryAlert.Low, null, null, null], Run(new(), Dis(15), Dis(16), Dis(15), Dis(16)));

    [Fact]
    public void Rising_to_25_resets() =>
        Assert.Equal([BatteryAlert.Low, null, BatteryAlert.Low], Run(new(), Dis(15), Dis(25), Dis(15)));

    [Fact]
    public void Asleep_and_not_found_are_ignored() =>
        Assert.Equal([BatteryAlert.Low, null, null, null],
            Run(new(), Dis(10),
                new(ConnectionState.Asleep, null, ChargingState.Unknown, DateTimeOffset.UnixEpoch),
                new(ConnectionState.NotFound, 90, ChargingState.Charging, DateTimeOffset.UnixEpoch),
                Dis(10)));

    [Fact]
    public void Charging_at_3_does_nothing() =>
        Assert.Null(new BatteryAlerts().Update(Chg(3)));

    [Fact]
    public void Unknown_charging_state_does_nothing() =>
        Assert.Null(new BatteryAlerts().Update(new(ConnectionState.Connected, 3, ChargingState.Unknown, DateTimeOffset.UnixEpoch)));

    [Fact]
    public void Full_after_charging_once() =>
        Assert.Equal([null, null, BatteryAlert.Full, null],
            Run(new(), Chg(80), Chg(99), Chg(100, ChargingState.Full), Chg(100, ChargingState.Full)));

    [Fact]
    public void Charging_at_100_counts_as_full() =>
        Assert.Equal([null, BatteryAlert.Full, null], Run(new(), Chg(98), Chg(100), Chg(100)));

    [Fact]
    public void Full_at_startup_is_silent() =>
        Assert.Equal([null, null], Run(new(), Chg(100, ChargingState.Full), Chg(100)));

    [Fact]
    public void Full_again_after_unplug_and_recharge() =>
        Assert.Equal([null, BatteryAlert.Full, null, null, BatteryAlert.Full],
            Run(new(), Chg(90), Chg(100, ChargingState.Full), Dis(97), Chg(95), Chg(100, ChargingState.Full)));

    [Fact]
    public void Full_without_unplug_fires_once() =>
        Assert.Equal([null, BatteryAlert.Full, null, null],
            Run(new(), Chg(90), Chg(100, ChargingState.Full), Chg(99), Chg(100, ChargingState.Full)));

    [Fact]
    public void Full_after_discharge_without_a_charging_reading() =>
        Assert.Equal([null, BatteryAlert.Full], Run(new(), Dis(80), Chg(100, ChargingState.Full)));

    [Fact]
    public void Replug_at_100_is_silent() =>
        Assert.Equal([null, null], Run(new(), Dis(100), Chg(100)));

    [Fact]
    public void Replug_at_100_after_full_is_silent() =>
        Assert.Equal([null, BatteryAlert.Full, null, null], Run(new(), Chg(90), Chg(100, ChargingState.Full), Dis(100), Chg(100)));

    [Fact]
    public void Full_after_discharge_with_asleep_in_between() =>
        Assert.Equal([null, null, BatteryAlert.Full],
            Run(new(), Dis(80), new BatteryStatus(ConnectionState.Asleep, null, ChargingState.Unknown, DateTimeOffset.UnixEpoch), Chg(100, ChargingState.Full)));
}
