using LogitechBattery.Battery;

namespace LogitechBattery.Tests;

public sealed class ChargeHistoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero); // Thursday

    private static HistoryRecord Record(DateTimeOffset time, ChargingState charging, int? percent = 50) =>
        TestData.Record(time, percent, charging);

    private static string? Describe(DateTimeOffset? lastCharge, ChargingState current = ChargingState.Discharging) =>
        ChargeHistory.Describe(lastCharge, TestData.Status(charging: current, at: Now), Now, TimeZoneInfo.Utc);

    [Fact]
    public void Last_charge_is_null_for_an_empty_history()
    {
        Assert.Null(ChargeHistory.LastCharge([], Now));
    }

    [Fact]
    public void Last_charge_is_null_when_the_mouse_only_discharged()
    {
        Assert.Null(ChargeHistory.LastCharge([Record(Now.AddHours(-1), ChargingState.Discharging, 70)], Now));
    }

    [Fact]
    public void A_charge_that_stopped_at_30_percent_counts()
    {
        var charged = Now.AddDays(-2);

        Assert.Equal(charged, ChargeHistory.LastCharge([Record(charged, ChargingState.Charging, 30)], Now));
    }

    [Fact]
    public void A_full_record_counts_as_a_charge()
    {
        var full = Now.AddDays(-1);

        Assert.Equal(full, ChargeHistory.LastCharge([Record(full, ChargingState.Full, 100)], Now));
    }

    [Fact]
    public void The_newest_charge_wins_even_when_records_are_out_of_order()
    {
        var newest = Now.AddHours(-5);
        var records = new[]
        {
            Record(newest, ChargingState.Charging, 80),
            Record(Now.AddDays(-4), ChargingState.Full, 100),
            Record(Now.AddHours(-1), ChargingState.Discharging, 78),
        };

        Assert.Equal(newest, ChargeHistory.LastCharge(records, Now));
    }

    [Fact]
    public void A_charge_dated_after_now_is_ignored()
    {
        var past = Now.AddDays(-1);
        var records = new[] { Record(past, ChargingState.Charging, 60), Record(Now.AddMinutes(10), ChargingState.Full, 100) };

        Assert.Equal(past, ChargeHistory.LastCharge(records, Now));
    }

    [Fact]
    public void Last_charge_line_is_hidden_while_charging_or_full()
    {
        Assert.Null(Describe(Now.AddDays(-3), ChargingState.Charging));
        Assert.Null(Describe(null, ChargingState.Full));
    }

    [Fact]
    public void Describe_says_not_seen_yet_without_a_charge()
    {
        Assert.Equal("Last charge: not seen yet", Describe(null));
    }

    [Fact]
    public void Describe_shows_only_the_time_for_today()
    {
        Assert.Equal("Last charge: 08:05", Describe(Now.AddHours(-4).AddMinutes(5)));
    }

    [Fact]
    public void Describe_shows_the_day_name_within_the_last_7_days()
    {
        Assert.Equal("Last charge: Tue 18:40", Describe(new DateTimeOffset(2026, 11, 3, 18, 40, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void Describe_shows_the_date_for_older_charges()
    {
        Assert.Equal("Last charge: 20 Oct 2026", Describe(new DateTimeOffset(2026, 10, 20, 9, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void Describe_uses_local_time()
    {
        var plus5 = TimeZoneInfo.CreateCustomTimeZone("plus5", TimeSpan.FromHours(5), "plus5", "plus5");
        var charged = new DateTimeOffset(2026, 11, 5, 3, 0, 0, TimeSpan.Zero); // 08:00 local

        Assert.Equal("Last charge: 08:00", ChargeHistory.Describe(charged, TestData.Status(at: Now), Now, plus5));
    }

    [Fact]
    public void Describe_without_a_status_still_shows_the_last_charge()
    {
        Assert.Equal("Last charge: 11:00", ChargeHistory.Describe(Now.AddHours(-1), null, Now, TimeZoneInfo.Utc));
    }
}
