using LogitechBattery.Battery;

namespace LogitechBattery.Tests;

public class BatteryAlertsTests
{
    private static readonly BatteryAlert? Quiet = null;

    private static BatteryStatus Discharging(int percent) => TestStatus.Reading(ConnectionState.Connected, percent, ChargingState.Discharging);
    private static BatteryStatus Charging(int percent) => TestStatus.Reading(ConnectionState.Connected, percent, ChargingState.Charging);
    private static BatteryStatus Full() => TestStatus.Reading(ConnectionState.Connected, 100, ChargingState.Full);
    private static BatteryStatus Asleep() => TestStatus.Reading(ConnectionState.Asleep, null, ChargingState.Unknown);

    private static BatteryAlert?[] AlertsFor(BatteryAlerts alerts, params BatteryStatus[] readings) =>
        readings.Select(alerts.Update).ToArray();

    private static BatteryAlert?[] AlertsFor(params BatteryStatus[] readings) => AlertsFor(new BatteryAlerts(), readings);

    [Fact]
    public void Discharge_alerts_low_at_15_and_critical_at_5_once_each() =>
        Assert.Equal([Quiet, BatteryAlert.Low, Quiet, BatteryAlert.Critical, Quiet],
            AlertsFor(Discharging(20), Discharging(15), Discharging(10), Discharging(5), Discharging(3)));

    [Fact]
    public void Starting_at_4_percent_alerts_critical_only() =>
        Assert.Equal([BatteryAlert.Critical, Quiet], AlertsFor(Discharging(4), Discharging(3)));

    [Fact]
    public void Starting_at_10_percent_alerts_low() =>
        Assert.Equal([BatteryAlert.Low, Quiet], AlertsFor(Discharging(10), Discharging(9)));

    [Fact]
    public void Dropping_from_30_to_4_alerts_critical_only() =>
        Assert.Equal([Quiet, BatteryAlert.Critical, Quiet], AlertsFor(Discharging(30), Discharging(4), Discharging(4)));

    [Fact]
    public void Charging_in_between_makes_low_alert_again() =>
        Assert.Equal([BatteryAlert.Low, Quiet, BatteryAlert.Low], AlertsFor(Discharging(10), Charging(10), Discharging(10)));

    [Fact]
    public void Reaching_full_makes_low_alert_again() =>
        Assert.Equal([BatteryAlert.Low, BatteryAlert.Full, BatteryAlert.Low], AlertsFor(Discharging(10), Full(), Discharging(10)));

    [Fact]
    public void Bouncing_around_15_percent_alerts_low_once() =>
        Assert.Equal([BatteryAlert.Low, Quiet, Quiet, Quiet], AlertsFor(Discharging(15), Discharging(16), Discharging(15), Discharging(16)));

    [Fact]
    public void Rising_to_25_percent_makes_low_alert_again() =>
        Assert.Equal([BatteryAlert.Low, Quiet, BatteryAlert.Low], AlertsFor(Discharging(15), Discharging(25), Discharging(15)));

    [Fact]
    public void Asleep_and_not_found_readings_are_ignored() =>
        Assert.Equal([BatteryAlert.Low, Quiet, Quiet, Quiet],
            AlertsFor(Discharging(10),
                Asleep(),
                TestStatus.Reading(ConnectionState.NotFound, 90, ChargingState.Charging),
                Discharging(10)));

    [Fact]
    public void Charging_at_3_percent_is_quiet() =>
        Assert.Equal([Quiet], AlertsFor(Charging(3)));

    [Fact]
    public void Unknown_charging_state_is_quiet() =>
        Assert.Equal([Quiet], AlertsFor(TestStatus.Reading(ConnectionState.Connected, 3, ChargingState.Unknown)));

    [Fact]
    public void Full_alerts_once_after_charging_from_below() =>
        Assert.Equal([Quiet, Quiet, BatteryAlert.Full, Quiet], AlertsFor(Charging(80), Charging(99), Full(), Full()));

    [Fact]
    public void Charging_at_100_percent_counts_as_full() =>
        Assert.Equal([Quiet, BatteryAlert.Full, Quiet], AlertsFor(Charging(98), Charging(100), Charging(100)));

    [Fact]
    public void Full_at_startup_is_quiet() =>
        Assert.Equal([Quiet, Quiet], AlertsFor(Full(), Charging(100)));

    [Fact]
    public void Full_alerts_again_after_unplugging_and_recharging() =>
        Assert.Equal([Quiet, BatteryAlert.Full, Quiet, Quiet, BatteryAlert.Full],
            AlertsFor(Charging(90), Full(), Discharging(97), Charging(95), Full()));

    [Fact]
    public void Full_without_unplugging_alerts_once() =>
        Assert.Equal([Quiet, BatteryAlert.Full, Quiet, Quiet], AlertsFor(Charging(90), Full(), Charging(99), Full()));

    [Fact]
    public void Full_alerts_after_discharging_without_a_charging_reading() =>
        Assert.Equal([Quiet, BatteryAlert.Full], AlertsFor(Discharging(80), Full()));

    [Fact]
    public void Replugging_at_100_percent_is_quiet() =>
        Assert.Equal([Quiet, Quiet], AlertsFor(Discharging(100), Charging(100)));

    [Fact]
    public void Replugging_at_100_percent_after_full_is_quiet() =>
        Assert.Equal([Quiet, BatteryAlert.Full, Quiet, Quiet], AlertsFor(Charging(90), Full(), Discharging(100), Charging(100)));

    [Fact]
    public void Full_alerts_after_discharging_with_asleep_in_between() =>
        Assert.Equal([Quiet, Quiet, BatteryAlert.Full], AlertsFor(Discharging(80), Asleep(), Full()));
}
