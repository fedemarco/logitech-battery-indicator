using System.Globalization;

namespace LogitechBattery.Battery;

/// <summary>Estimates the remaining battery time from the recent discharge history.</summary>
public static class RemainingTime
{
    public static readonly TimeSpan Window = HistoryChart.Week;
    private static readonly TimeSpan MinimumSpan = TimeSpan.FromDays(2);
    private const int MinimumDropPercent = 5;

    /// <summary>
    /// Average percent lost per calendar day over the last 7 days, or null when there is too little data.
    /// Time while the mouse sleeps counts: the level after a gap already includes what happened in it.
    /// Charging, full, or a rise in percent (a charge the app did not see) ends a discharge segment.
    /// Known limit: an unseen charge that ends below the previous reading shows no rise, so it goes
    /// undetected and makes the rate too low.
    /// </summary>
    public static double? DrainPercentPerDay(IReadOnlyList<HistoryRecord> records, DateTimeOffset now)
    {
        var totalDrop = 0;
        var totalSpan = TimeSpan.Zero;
        DischargeSegment? open = null;

        void CloseSegment()
        {
            if (open is { } segment)
            {
                totalDrop += segment.Drop;
                totalSpan += segment.Span;
            }
            open = null;
        }

        var recent = records.Where(record => record.Time >= now - Window && record.Time <= now);
        foreach (var record in recent.OrderBy(record => record.Time))
        {
            if (record.Charging is ChargingState.Charging or ChargingState.Full)
            {
                CloseSegment();
                continue;
            }
            if (record.Charging != ChargingState.Discharging || record.Percent is not { } percent)
                continue;

            if (open is { } current && percent > current.LastPercent)
                CloseSegment();

            open = open is { } continuing
                ? continuing.Extend(record.Time, percent)
                : new DischargeSegment(record.Time, percent, record.Time, percent);
        }
        CloseSegment();

        if (totalSpan < MinimumSpan || totalDrop < MinimumDropPercent)
            return null;
        return totalDrop / totalSpan.TotalDays;
    }

    public static string? RemainingText(BatteryStatus current, double? drainPercentPerDay)
    {
        if (drainPercentPerDay is not { } rate
            || current is not { Connection: ConnectionState.Connected, Charging: ChargingState.Discharging, Percent: { } percent })
            return null;

        var hours = percent / rate * TimeSpan.HoursPerDay;
        var rounded = Math.Round(hours, MidpointRounding.AwayFromZero);
        return rounded == 0 ? "Less than 1 h left" : string.Create(CultureInfo.InvariantCulture, $"{rounded} h left");
    }

    /// <summary>An unbroken stretch of discharging, from its first to its latest reading.</summary>
    private sealed record DischargeSegment(DateTimeOffset FirstTime, int FirstPercent, DateTimeOffset LastTime, int LastPercent)
    {
        public int Drop => FirstPercent - LastPercent;
        public TimeSpan Span => LastTime - FirstTime;
        public DischargeSegment Extend(DateTimeOffset time, int percent) => this with { LastTime = time, LastPercent = percent };
    }
}
