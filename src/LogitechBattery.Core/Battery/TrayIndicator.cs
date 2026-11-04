namespace LogitechBattery.Battery;

/// <summary>What the tray icon shows: the name of an embedded gauge icon (used as-is, colors included).</summary>
public sealed record TrayIndicator(string Icon)
{
    private static readonly TrayIndicator Unknown = new("battery_unknown");

    /// <summary>Maps a status to the indicator; null means the read failed.</summary>
    public static TrayIndicator From(BatteryStatus? status)
    {
        if (status is not { Connection: ConnectionState.Connected, Percent: { } percent })
            return Unknown;

        var bucket = percent switch
        {
            >= 80 => 100,
            >= 60 => 75,
            >= 40 => 50,
            >= 20 => 25,
            _ => 0,
        };
        var suffix = status.Charging == ChargingState.Charging ? "_chrg" : "";

        return new TrayIndicator($"battery{bucket}{suffix}");
    }
}
