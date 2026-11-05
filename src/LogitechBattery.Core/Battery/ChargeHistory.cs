using System.Globalization;

namespace LogitechBattery.Battery;

/// <summary>When the mouse was last charged, as the overlay shows it.</summary>
public static class ChargeHistory
{
    /// <summary>The time of the newest Charging or Full record up to now, at any level; null when there is none.</summary>
    public static DateTimeOffset? LastCharge(IEnumerable<HistoryRecord> records, DateTimeOffset now) =>
        records
            .Where(record => record.Time <= now && record.Charging is ChargingState.Charging or ChargingState.Full)
            .Select(record => (DateTimeOffset?)record.Time)
            .Max();

    /// <summary>
    /// "Last charge: 18:40" (today), "... Tue 18:40" (within 7 days), "... 3 Nov 2026" or "... not seen yet";
    /// null while the mouse is charging or full, when the line is hidden.
    /// </summary>
    public static string? Describe(DateTimeOffset? lastCharge, BatteryStatus? current, DateTimeOffset now, TimeZoneInfo zone)
    {
        const string prefix = "Last charge: ";
        if (current?.Charging is ChargingState.Charging or ChargingState.Full)
            return null;
        if (lastCharge is not { } time)
            return prefix + "not seen yet";

        var local = TimeZoneInfo.ConvertTime(time, zone);
        var format = local.Date == TimeZoneInfo.ConvertTime(now, zone).Date ? "HH:mm"
            : now - time < HistoryChart.Week ? "ddd HH:mm"
            : "d MMM yyyy";
        return prefix + local.ToString(format, CultureInfo.InvariantCulture);
    }
}
