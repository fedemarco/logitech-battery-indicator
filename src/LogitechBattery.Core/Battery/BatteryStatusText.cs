namespace LogitechBattery.Battery;

/// <summary>The shortest text for a status in a list of devices (the tray tooltip and the "Show in tray" menu).</summary>
public static class BatteryStatusText
{
    public static string Short(BatteryStatus? status) => status switch
    {
        null => "Read failed",
        { Connection: ConnectionState.Connected, Percent: { } percent } => $"{percent}%",
        _ => status.ToDisplayString(),
    };
}
