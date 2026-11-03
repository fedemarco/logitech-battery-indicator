namespace LogitechBattery.Battery;

public enum IndicatorColor { Green, Orange, Red, Gray }

/// <summary>What the tray icon shows: badge text, badge color and whether to draw the charging bolt.</summary>
public sealed record TrayIndicator(string Text, IndicatorColor Color, bool Charging)
{
    private const string Unknown = "–";

    /// <summary>Maps a status to the indicator; null means the read failed.</summary>
    public static TrayIndicator From(BatteryStatus? status)
    {
        if (status is not { Connection: ConnectionState.Connected, Percent: { } percent, Level: { } level })
            return new TrayIndicator(Unknown, IndicatorColor.Gray, false);

        var color = level switch
        {
            BatteryLevel.Good => IndicatorColor.Green,
            BatteryLevel.Low => IndicatorColor.Orange,
            _ => IndicatorColor.Red,
        };
        return new TrayIndicator(Math.Clamp(percent, 0, 100).ToString(), color, status.Charging == ChargingState.Charging);
    }
}
