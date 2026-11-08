using LogitechBattery.Battery;

namespace LogitechBattery.Tests;

public sealed class HistoryChartTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);

    private static HistoryRecord Record(TimeSpan before, int? percent = 80, ChargingState charging = ChargingState.Discharging) =>
        TestData.Record(Now - before, percent, charging);

    private static HistoryChart Day(params HistoryRecord[] records) => HistoryChart.Build(records, Now, HistoryChart.Day);

    [Fact]
    public void Empty_history_has_no_points_and_no_joins()
    {
        var chart = Day();

        Assert.Empty(chart.Points);
        Assert.Empty(chart.Joins);
    }

    [Fact]
    public void A_single_record_is_a_point_without_joins()
    {
        var chart = Day(Record(TimeSpan.FromHours(1), 64));

        Assert.Equal([64], chart.Points.Select(point => point.Percent));
        Assert.Empty(chart.Joins);
    }

    [Fact]
    public void Records_less_than_20_minutes_apart_are_joined_solid()
    {
        var chart = Day(Record(TimeSpan.FromMinutes(30), 80), Record(TimeSpan.FromMinutes(15), 79));

        Assert.True(Assert.Single(chart.Joins).IsSolid);
    }

    [Fact]
    public void Records_exactly_20_minutes_apart_are_still_joined_solid()
    {
        var chart = Day(Record(TimeSpan.FromMinutes(40), 80), Record(TimeSpan.FromMinutes(20), 79));

        Assert.True(Assert.Single(chart.Joins).IsSolid);
    }

    [Fact]
    public void Records_more_than_20_minutes_apart_are_joined_dashed()
    {
        var chart = Day(Record(TimeSpan.FromMinutes(41), 80), Record(TimeSpan.FromMinutes(20), 79));

        Assert.False(Assert.Single(chart.Joins).IsSolid);
    }

    [Fact]
    public void When_every_pair_is_further_apart_than_20_minutes_all_joins_are_dashed()
    {
        var chart = Day(Record(TimeSpan.FromHours(9), 90), Record(TimeSpan.FromHours(6), 70), Record(TimeSpan.FromHours(2), 50));

        Assert.Equal(2, chart.Joins.Count);
        Assert.All(chart.Joins, join => Assert.False(join.IsSolid));
    }

    [Fact]
    public void Records_out_of_order_are_sorted_by_time()
    {
        var chart = Day(Record(TimeSpan.FromHours(1), 60), Record(TimeSpan.FromHours(3), 80), Record(TimeSpan.FromHours(2), 70));

        Assert.Equal([80, 70, 60], chart.Points.Select(point => point.Percent));
    }

    [Fact]
    public void Records_in_the_future_are_dropped()
    {
        var chart = Day(Record(TimeSpan.FromHours(1), 60), Record(-TimeSpan.FromMinutes(5), 99));

        Assert.Equal([60], chart.Points.Select(point => point.Percent));
    }

    [Fact]
    public void Records_older_than_the_window_are_dropped()
    {
        var chart = Day(Record(TimeSpan.FromHours(25), 90), Record(TimeSpan.FromHours(23), 85));

        Assert.Equal([85], chart.Points.Select(point => point.Percent));
    }

    [Fact]
    public void A_record_exactly_at_the_start_of_the_window_is_kept()
    {
        var chart = Day(Record(HistoryChart.Day, 90));

        Assert.Equal([90], chart.Points.Select(point => point.Percent));
    }

    [Fact]
    public void A_record_exactly_at_now_is_kept()
    {
        var chart = Day(Record(TimeSpan.Zero, 90));

        Assert.Equal([90], chart.Points.Select(point => point.Percent));
    }

    [Fact]
    public void The_week_window_keeps_records_the_day_window_drops()
    {
        var records = new[] { Record(TimeSpan.FromDays(3), 90), Record(TimeSpan.FromHours(1), 40) };

        Assert.Equal(2, HistoryChart.Build(records, Now, HistoryChart.Week).Points.Count);
        Assert.Single(HistoryChart.Build(records, Now, HistoryChart.Day).Points);
    }

    [Fact]
    public void Records_without_a_percent_are_ignored()
    {
        var chart = Day(Record(TimeSpan.FromHours(2), percent: null), Record(TimeSpan.FromHours(1), 55));

        Assert.Equal([55], chart.Points.Select(point => point.Percent));
    }

    [Fact]
    public void Hour_ticks_on_the_spring_forward_day_stay_on_the_local_hours()
    {
        var zone = ZoneWithDst(springForwardHour: 2); // Sunday 29 March: 02:00 becomes 03:00
        var now = new DateTimeOffset(2026, 3, 29, 20, 0, 0, TimeSpan.Zero);

        var ticks = HistoryChart.HourTicks(now, zone);

        Assert.Equal(["00", "06", "12", "18"], ticks.Select(tick => tick.Label));
        Assert.Equal(
            [Utc(2026, 3, 28, 23), Utc(2026, 3, 29, 4), Utc(2026, 3, 29, 10), Utc(2026, 3, 29, 16)],
            ticks.Select(tick => tick.Time));
    }

    [Fact]
    public void Hour_ticks_on_the_fall_back_day_stay_on_the_local_hours()
    {
        var zone = ZoneWithDst(springForwardHour: 2); // Sunday 25 October: 03:00 becomes 02:00
        var now = new DateTimeOffset(2026, 10, 25, 20, 0, 0, TimeSpan.Zero);

        var ticks = HistoryChart.HourTicks(now, zone);

        Assert.Equal(["00", "06", "12", "18"], ticks.Select(tick => tick.Label));
        Assert.Equal(
            [Utc(2026, 10, 24, 22), Utc(2026, 10, 25, 5), Utc(2026, 10, 25, 11), Utc(2026, 10, 25, 17)],
            ticks.Select(tick => tick.Time));
    }

    private static DateTimeOffset Utc(int year, int month, int day, int hour) => new(year, month, day, hour, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Hour_ticks_fall_every_6_local_hours()
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("plus2", TimeSpan.FromHours(2), "plus2", "plus2");

        var ticks = HistoryChart.HourTicks(Now, zone); // 14:00 local

        Assert.Equal(["18", "00", "06", "12"], ticks.Select(tick => tick.Label));
    }

    [Theory]
    [InlineData(2026, 3, 31, 2)]  // week after the spring-forward Sunday 29 March
    [InlineData(2026, 10, 27, 2)] // week after the fall-back Sunday 25 October
    public void Day_ticks_are_strictly_increasing_local_midnights_across_a_DST_change(int year, int month, int day, int transitionHour)
    {
        var zone = ZoneWithDst(transitionHour);
        var now = new DateTimeOffset(year, month, day, 12, 0, 0, TimeSpan.Zero);

        var ticks = HistoryChart.DayTicks(now, zone);

        Assert.Equal(7, ticks.Count);
        Assert.All(ticks, tick => Assert.Equal(TimeSpan.Zero, TimeZoneInfo.ConvertTime(tick.Time, zone).TimeOfDay));
        Assert.All(ticks, tick => Assert.True(tick.Time > now - HistoryChart.Week && tick.Time <= now));
        // 23 to 25 h between neighbours also proves the ticks are strictly increasing
        Assert.All(ticks.Skip(1).Zip(ticks, (later, earlier) => later.Time - earlier.Time), gap => Assert.InRange(gap, TimeSpan.FromHours(23), TimeSpan.FromHours(25)));
    }

    [Fact]
    public void A_missing_local_midnight_gets_its_tick_at_01_00_the_first_valid_instant_of_that_day()
    {
        var zone = ZoneWithDst(springForwardHour: 0); // Sunday 29 March has no 00:00
        var now = new DateTimeOffset(2026, 3, 31, 12, 0, 0, TimeSpan.Zero);

        var ticks = HistoryChart.DayTicks(now, zone);

        var sunday = ticks.Single(tick => tick.Label == "Sun");
        Assert.Equal(new DateTime(2026, 3, 29, 1, 0, 0), TimeZoneInfo.ConvertTime(sunday.Time, zone).DateTime);
        Assert.Equal(ticks.Select(tick => tick.Time).Order(), ticks.Select(tick => tick.Time));
    }

    [Fact]
    public void Day_ticks_are_labelled_with_the_short_local_day_name()
    {
        var ticks = HistoryChart.DayTicks(Now, TimeZoneInfo.Utc); // Thu 5 Nov 2026

        Assert.Equal(["Fri", "Sat", "Sun", "Mon", "Tue", "Wed", "Thu"], ticks.Select(tick => tick.Label));
    }

    // Standard time UTC+1; DST from the last Sunday of March (starting at the given local hour) to the last Sunday of October, 03:00.
    private static TimeZoneInfo ZoneWithDst(int springForwardHour)
    {
        var start = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, springForwardHour, 0, 0), 3, 5, DayOfWeek.Sunday);
        var end = TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 3, 0, 0), 10, 5, DayOfWeek.Sunday);
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(DateTime.MinValue.Date, DateTime.MaxValue.Date, TimeSpan.FromHours(1), start, end);
        return TimeZoneInfo.CreateCustomTimeZone("dst-test", TimeSpan.FromHours(1), "dst-test", "std", "dst", [rule]);
    }
}
