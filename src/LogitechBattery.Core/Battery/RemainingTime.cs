using System.Globalization;

namespace LogitechBattery.Battery;

/// <summary>Estimates the remaining battery time from the recent discharge history.</summary>
public static class RemainingTime
{
    public static readonly TimeSpan Window = TimeSpan.FromDays(7);
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
        (Reading First, Reading Last)? segment = null;

        void CloseSegment()
        {
            if (segment is { } open)
            {
                totalDrop += open.First.Percent - open.Last.Percent;
                totalSpan += open.Last.Time - open.First.Time;
            }
            segment = null;
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

            if (segment is { Last: var previous } && percent > previous.Percent)
                CloseSegment();

            var reading = new Reading(record.Time, percent);
            segment = (segment?.First ?? reading, reading);
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

        var hours = percent / rate * 24;
        var rounded = Math.Round(hours, MidpointRounding.AwayFromZero);
        return rounded == 0 ? "Less than 1 h left" : string.Create(CultureInfo.InvariantCulture, $"{rounded} h left");
    }

    private sealed record Reading(DateTimeOffset Time, int Percent);
}
