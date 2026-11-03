namespace LogitechBattery.Battery;

public sealed record BatteryStatus(
    ConnectionState Connection,
    int? Percent,
    ChargingState Charging,
    DateTimeOffset ReadAt)
{
    public BatteryLevel? Level => Percent switch
    {
        null => null,
        >= BatteryThresholds.Good => BatteryLevel.Good,
        >= BatteryThresholds.Low => BatteryLevel.Low,
        _ => BatteryLevel.Critical,
    };

    /// <summary>Short text such as "85% · Discharging", "Charging · 60%", "Mouse asleep".</summary>
    public string ToDisplayString()
    {
        switch (Connection)
        {
            case ConnectionState.Asleep:
                return "Mouse asleep";
            case ConnectionState.NotFound:
                return "Mouse not found";
        }

        var percent = Percent is { } p ? $"{p}%" : null;
        return (Charging, percent) switch
        {
            (ChargingState.Charging, not null) => $"Charging · {percent}",
            (ChargingState.Charging, null) => "Charging",
            (ChargingState.Full, not null) => $"Full · {percent}",
            (ChargingState.Full, null) => "Full",
            (ChargingState.Discharging, not null) => $"{percent} · Discharging",
            (_, not null) => percent,
            _ => "Battery unknown",
        };
    }
}
