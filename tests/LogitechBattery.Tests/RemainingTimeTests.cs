using LogitechBattery.Battery;

namespace LogitechBattery.Tests;

public sealed class RemainingTimeTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);

    private static HistoryRecord Record(
        double daysAgo,
        int? percent,
        ChargingState charging = ChargingState.Discharging,
        ConnectionState connection = ConnectionState.Connected) =>
        TestData.Record(Now - TimeSpan.FromDays(daysAgo), percent, charging, connection);

    private static double? Rate(params HistoryRecord[] records) => RemainingTime.DrainPercentPerDay(records, Now);

    private static BatteryStatus Current(
        int? percent = 50,
        ChargingState charging = ChargingState.Discharging,
        ConnectionState connection = ConnectionState.Connected) =>
        TestData.Status(percent, charging, connection, Now);

    [Fact]
    public void Empty_history_gives_no_rate() => Assert.Null(Rate());

    [Fact]
    public void A_single_record_gives_no_rate() => Assert.Null(Rate(Record(3, 80)));

    [Fact]
    public void Rate_is_the_drop_per_day()
    {
        Assert.Equal(10, Rate(Record(4, 90), Record(0, 50)));
    }

    [Fact]
    public void Exactly_two_days_and_five_percent_qualify()
    {
        Assert.Equal(2.5, Rate(Record(2, 80), Record(0, 75)));
    }

    [Fact]
    public void Less_than_two_days_gives_no_rate() => Assert.Null(Rate(Record(1.9, 80), Record(0, 40)));

    [Fact]
    public void Less_than_five_percent_gives_no_rate() => Assert.Null(Rate(Record(5, 80), Record(0, 76)));

    [Fact]
    public void No_drop_gives_no_rate_instead_of_dividing_by_zero() => Assert.Null(Rate(Record(5, 80), Record(0, 80)));

    [Fact]
    public void A_rise_while_discharging_starts_a_new_segment() => Assert.Null(Rate(Record(5, 70), Record(0, 72)));

    [Fact]
    public void A_small_rise_splits_the_segment_and_both_drops_count()
    {
        var rate = Rate(Record(5, 90), Record(4, 80), Record(3, 81), Record(0, 51));

        Assert.Equal(10, rate); // 10 % over 1 day plus 30 % over 3 days
    }

    [Fact]
    public void Unseen_charge_during_a_gap_starts_a_new_segment()
    {
        var rate = Rate(Record(6, 60), Record(4, 100), Record(0, 80));

        Assert.Equal(5, rate); // only the 100 -> 80 drop over 4 days
    }

    [Fact]
    public void A_record_exactly_seven_days_old_is_inside_the_window()
    {
        Assert.Equal(10, Rate(Record(7, 80), Record(0, 10)));
    }

    [Fact]
    public void Records_older_than_seven_days_are_ignored()
    {
        Assert.Equal(10, Rate(Record(8, 100), Record(4, 90), Record(0, 50)));
    }

    [Fact]
    public void Records_without_a_percent_are_skipped_but_their_time_counts()
    {
        var rate = Rate(
            Record(6, 90),
            Record(5, null, ChargingState.Unknown, ConnectionState.Asleep),
            Record(0, 60));

        Assert.Equal(5, rate);
    }

    [Fact]
    public void A_charge_in_the_middle_splits_the_segments()
    {
        var rate = Rate(
            Record(6, 90), Record(4, 70),
            Record(3, 40, ChargingState.Charging), Record(2.5, 100, ChargingState.Full),
            Record(2, 100), Record(0, 80));

        Assert.Equal(40 / 4.0, rate); // 20 % in 2 days plus 20 % in 2 days; charging time is not counted
    }

    [Fact]
    public void Records_after_now_are_ignored()
    {
        Assert.Equal(10, Rate(Record(4, 90), Record(0, 50), Record(-1, 10)));
    }

    [Fact]
    public void Records_out_of_time_order_are_sorted_first()
    {
        Assert.Equal(10, Rate(Record(0, 50), Record(4, 90)));
    }

    [Theory]
    [InlineData(40, 10, "96 h left")]
    [InlineData(100, 6, "400 h left")] // 16.67 days at 6 %/day is 400 h
    [InlineData(1, 24, "1 h left")]
    [InlineData(1, 36, "1 h left")] // 0.67 h rounds to 1
    [InlineData(1, 48, "1 h left")] // exactly 0.5 h rounds up
    public void Remaining_text_shows_whole_hours(int percent, double rate, string expected) =>
        Assert.Equal(expected, RemainingTime.RemainingText(Current(percent: percent), rate));

    [Fact]
    public void Below_half_an_hour_says_less_than_one_hour() =>
        Assert.Equal("Less than 1 h left", RemainingTime.RemainingText(Current(percent: 1), 50));

    [Fact]
    public void No_rate_gives_no_text() => Assert.Null(RemainingTime.RemainingText(Current(), null));

    [Theory]
    [InlineData(ChargingState.Charging)]
    [InlineData(ChargingState.Full)]
    [InlineData(ChargingState.Unknown)]
    public void Not_discharging_gives_no_text(ChargingState charging) =>
        Assert.Null(RemainingTime.RemainingText(Current(charging: charging), 10));

    [Theory]
    [InlineData(ConnectionState.Asleep)]
    [InlineData(ConnectionState.NotFound)]
    public void Not_connected_gives_no_text(ConnectionState connection) =>
        Assert.Null(RemainingTime.RemainingText(Current(percent: null, connection: connection), 10));

    [Theory]
    [InlineData(ConnectionState.Asleep)]
    [InlineData(ConnectionState.NotFound)]
    public void Not_connected_with_a_percent_gives_no_text(ConnectionState connection) =>
        Assert.Null(RemainingTime.RemainingText(Current(percent: 50, connection: connection), 10));

    [Fact]
    public void Unknown_percent_gives_no_text() =>
        Assert.Null(RemainingTime.RemainingText(Current(percent: null), 10));
}
