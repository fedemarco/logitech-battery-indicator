namespace LogitechBattery.Battery;

public sealed record BatteryStatus(
    ConnectionState Connection,
    int? Percent,
    ChargingState Charging,
    DateTimeOffset ReadAt)
{
    public const int FullPercent = 100;

    public BatteryLevel? Level => Percent switch
    {
        null => null,
        <= BatteryThresholds.Critical => BatteryLevel.Critical,
        <= BatteryThresholds.Low => BatteryLevel.Low,
        _ => BatteryLevel.Good,
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

        var percentText = Percent is { } percent ? $"{percent}%" : null;
        return (Charging, percentText) switch
        {
            (ChargingState.Charging, not null) => $"Charging · {percentText}",
            (ChargingState.Charging, null) => "Charging",
            (ChargingState.Full, not null) => $"Full · {percentText}",
            (ChargingState.Full, null) => "Full",
            (ChargingState.Discharging, not null) => $"{percentText} · Discharging",
            (ChargingState.Unknown, not null) => percentText,
            _ => "Battery unknown",
        };
    }
}
