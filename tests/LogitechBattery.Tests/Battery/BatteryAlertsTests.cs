using LogitechBattery.Battery;

namespace LogitechBattery.Tests;

public class BatteryAlertsTests
{
    private static readonly BatteryAlert? Quiet = null;

    private static BatteryStatus Discharging(int percent) => TestData.Status(percent);
    private static BatteryStatus Charging(int percent) => TestData.Status(percent, ChargingState.Charging);
    private static BatteryStatus Full() => TestData.Status(100, ChargingState.Full);
    private static BatteryStatus Asleep() => TestData.Status(null, ChargingState.Unknown, ConnectionState.Asleep);

    public static IEnumerable<object[]> LowChoices => AlertSettings.LowChoices.Select(lowPercent => new object[] { lowPercent });

    private static AlertSettings LowAt(int lowPercent) => AlertSettings.Default with { LowPercent = lowPercent };

    private static BatteryAlert?[] AlertsFor(BatteryAlerts alerts, AlertSettings settings, params BatteryStatus[] readings) =>
        readings.Select(reading => alerts.Update(reading, settings)).ToArray();

    private static BatteryAlert?[] AlertsFor(AlertSettings settings, params BatteryStatus[] readings) =>
        AlertsFor(new BatteryAlerts(), settings, readings);

    private static BatteryAlert?[] AlertsFor(params BatteryStatus[] readings) => AlertsFor(AlertSettings.Default, readings);

    [Fact]
    public void Default_low_level_is_one_of_the_choices() =>
        Assert.Contains(AlertSettings.Default.LowPercent, AlertSettings.LowChoices);

    [Theory]
    [MemberData(nameof(LowChoices))]
    public void Low_alert_fires_at_the_chosen_low_level(int lowPercent) =>
        Assert.Equal([Quiet, BatteryAlert.Low, Quiet],
            AlertsFor(LowAt(lowPercent), Discharging(lowPercent + 1), Discharging(lowPercent), Discharging(lowPercent - 1)));

    [Theory]
    [MemberData(nameof(LowChoices))]
    public void Critical_alert_fires_at_5_percent_for_every_low_choice(int lowPercent) =>
        Assert.Equal([BatteryAlert.Low, Quiet, BatteryAlert.Critical],
            AlertsFor(LowAt(lowPercent), Discharging(lowPercent), Discharging(6), Discharging(5)));

    [Theory]
    [MemberData(nameof(LowChoices))]
    public void Rising_10_above_the_low_level_makes_low_alert_again(int lowPercent) =>
        Assert.Equal([BatteryAlert.Low, Quiet, BatteryAlert.Low],
            AlertsFor(LowAt(lowPercent), Discharging(lowPercent), Discharging(lowPercent + 10), Discharging(lowPercent)));

    [Theory]
    [MemberData(nameof(LowChoices))]
    public void Rising_less_than_10_above_the_low_level_alerts_low_once(int lowPercent) =>
        Assert.Equal([BatteryAlert.Low, Quiet, Quiet],
            AlertsFor(LowAt(lowPercent), Discharging(lowPercent), Discharging(lowPercent + 9), Discharging(lowPercent)));

    [Fact]
    public void Raising_the_low_level_alerts_at_the_next_reading_below_it()
    {
        var alerts = new BatteryAlerts();

        Assert.Null(alerts.Update(Discharging(18), LowAt(15)));
        Assert.Equal(BatteryAlert.Low, alerts.Update(Discharging(18), LowAt(20)));
    }

    [Fact]
    public void Lowering_the_low_level_after_an_alert_warns_again_at_the_new_level()
    {
        var alerts = new BatteryAlerts();

        Assert.Equal(BatteryAlert.Low, alerts.Update(Discharging(20), LowAt(20)));
        Assert.Null(alerts.Update(Discharging(21), LowAt(10)));
        Assert.Null(alerts.Update(Discharging(11), LowAt(10)));
        Assert.Equal(BatteryAlert.Low, alerts.Update(Discharging(10), LowAt(10)));
    }

    [Fact]
    public void Full_charge_alert_off_stays_quiet_when_a_charge_completes() =>
        Assert.Equal([Quiet, Quiet, Quiet],
            AlertsFor(AlertSettings.Default with { FullChargeAlert = false }, Charging(90), Full(), Full()));

    [Fact]
    public void Turning_the_full_charge_alert_on_after_the_charge_completed_does_not_alert_late()
    {
        var alerts = new BatteryAlerts();
        var alertOff = AlertSettings.Default with { FullChargeAlert = false };

        Assert.Equal([Quiet, Quiet], AlertsFor(alerts, alertOff, Charging(90), Full()));
        Assert.Equal([Quiet], AlertsFor(alerts, AlertSettings.Default, Full()));
    }

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
                TestData.Status(90, ChargingState.Charging, ConnectionState.NotFound),
                Discharging(10)));

    // Guards the coupling "low level < new-cycle level" for configurable thresholds.
    [Theory]
    [MemberData(nameof(LowChoices))]
    public void Discharging_from_100_to_10_above_the_low_level_never_alerts(int lowPercent)
    {
        var readings = Enumerable.Range(lowPercent + 10, 101 - (lowPercent + 10)).Reverse().Select(Discharging).ToArray();

        Assert.All(AlertsFor(LowAt(lowPercent), readings), alert => Assert.Null(alert));
    }

    [Fact]
    public void Charging_at_3_percent_is_quiet() =>
        Assert.Equal([Quiet], AlertsFor(Charging(3)));

    [Fact]
    public void Unknown_charging_state_is_quiet() =>
        Assert.Equal([Quiet], AlertsFor(TestData.Status(3, ChargingState.Unknown)));

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
