namespace LogitechBattery.Battery;

public sealed record BatteryStatus(
    ConnectionState Connection,
    int? Percent,
    ChargingState Charging,
    DateTimeOffset ReadAt)
{
    public const int FullPercent = 100;

    /// <summary>Short text such as "85% · Discharging", "Charging · 60%", "Asleep". The device name is shown with it.</summary>
    public string ToDisplayString()
    {
        switch (Connection)
        {
            case ConnectionState.Asleep:
                return "Asleep";
            case ConnectionState.NotFound:
                return "Not found";
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
