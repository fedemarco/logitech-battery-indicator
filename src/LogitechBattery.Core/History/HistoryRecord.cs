using LogitechBattery.Battery;

namespace LogitechBattery.History;

/// <summary>One line of the battery history file.</summary>
public sealed record HistoryRecord(
    DateTimeOffset Time,
    int? Percent,
    ChargingState Charging,
    ConnectionState Connection);
