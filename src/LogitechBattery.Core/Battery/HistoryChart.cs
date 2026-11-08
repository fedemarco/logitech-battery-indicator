using System.Globalization;

namespace LogitechBattery.Battery;

public sealed record ChartPoint(DateTimeOffset Time, int Percent);

/// <summary>A line between two consecutive points; dashed when the app was not watching in between.</summary>
public sealed record ChartJoin(ChartPoint From, ChartPoint To, bool IsSolid);

public sealed record ChartTick(DateTimeOffset Time, string Label);

/// <summary>The points and joins of the battery graph for one time window.</summary>
public sealed record HistoryChart(IReadOnlyList<ChartPoint> Points, IReadOnlyList<ChartJoin> Joins)
{
    public static readonly TimeSpan Day = TimeSpan.FromHours(24);
    public static readonly TimeSpan Week = TimeSpan.FromDays(7);

    // The history writes at least one record per heartbeat, so a longer silence means the app was not watching.
    private static readonly TimeSpan MaxSolidGap = BatteryHistory.HeartbeatInterval + TimeSpan.FromMinutes(5);
    private static readonly TimeSpan HourTickStep = TimeSpan.FromHours(6);

    /// <summary>Points inside [now - window, now] in time order; records after now (clock skew) are dropped.</summary>
    public static HistoryChart Build(IEnumerable<HistoryRecord> records, DateTimeOffset now, TimeSpan window)
    {
        var points = records
            .Where(record => record.Percent is not null && record.Time >= now - window && record.Time <= now)
            .OrderBy(record => record.Time)
            .Select(record => new ChartPoint(record.Time, record.Percent!.Value))
            .ToList();

        var joins = points.Zip(points.Skip(1), (from, to) => new ChartJoin(from, to, to.Time - from.Time <= MaxSolidGap)).ToList();
        return new HistoryChart(points, joins);
    }

    /// <summary>Ticks every 6 local hours ("00", "06", ...) inside the 24 h window ending now.</summary>
    public static IReadOnlyList<ChartTick> HourTicks(DateTimeOffset now, TimeZoneInfo zone) =>
        LocalTicks(now, Day, HourTickStep, "HH", zone);

    /// <summary>Ticks at local midnight ("Mon", ...) inside the 7 d window ending now.</summary>
    public static IReadOnlyList<ChartTick> DayTicks(DateTimeOffset now, TimeZoneInfo zone) =>
        LocalTicks(now, Week, TimeSpan.FromDays(1), "ddd", zone);

    // Computed from local wall-clock times so a DST change moves no tick off its hour.
    private static List<ChartTick> LocalTicks(DateTimeOffset now, TimeSpan window, TimeSpan step, string format, TimeZoneInfo zone)
    {
        var start = now - window;
        var lastDay = TimeZoneInfo.ConvertTime(now, zone).Date;
        var ticks = new List<ChartTick>();
        for (var day = TimeZoneInfo.ConvertTime(start, zone).Date; day <= lastDay; day = day.AddDays(1))
        {
            foreach (var local in LocalTimesOfDay(day, step))
            {
                var time = new DateTimeOffset(local, zone.GetUtcOffset(local));
                if (time >= start && time <= now)
                    ticks.Add(new ChartTick(time, local.ToString(format, CultureInfo.InvariantCulture)));
            }
        }
        return ticks;
    }

    private static IEnumerable<DateTime> LocalTimesOfDay(DateTime day, TimeSpan step)
    {
        for (var local = day; local.Date == day; local += step)
            yield return local;
    }
}
