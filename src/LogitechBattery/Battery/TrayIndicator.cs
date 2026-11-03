namespace LogitechBattery.Battery;

public enum IndicatorTint { None, Green, Orange, Red }

/// <summary>What the tray icon shows: the name of an embedded gauge icon and how to tint it.</summary>
public sealed record TrayIndicator(string Icon, IndicatorTint Tint)
{
    private static readonly TrayIndicator Unknown = new("battery_unknown", IndicatorTint.None);

    /// <summary>Maps a status to the indicator; null means the read failed.</summary>
    public static TrayIndicator From(BatteryStatus? status)
    {
        if (status is not { Connection: ConnectionState.Connected, Percent: { } percent, Level: { } level })
            return Unknown;

        var p = Math.Clamp(percent, 0, 100);
        var bucket = p >= 80 ? 100 : p >= 60 ? 75 : p >= 40 ? 50 : p >= 20 ? 25 : 0;
        var suffix = status.Charging == ChargingState.Charging ? "_chrg" : "";

        var tint = level switch
        {
            BatteryLevel.Good => IndicatorTint.Green,
            BatteryLevel.Low => IndicatorTint.Orange,
            _ => IndicatorTint.Red,
        };
        return new TrayIndicator($"battery{bucket}{suffix}", tint);
    }
}
