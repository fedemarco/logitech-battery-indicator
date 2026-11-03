namespace LogitechBattery.Battery;

public enum IndicatorTint { Theme, Orange, Red }

/// <summary>What the tray icon shows: a Windows battery glyph (icon font codepoint) and its tint.</summary>
public sealed record TrayIndicator(char Glyph, IndicatorTint Tint)
{
    private const char Unknown = '\uE996';
    private const char Battery0 = '\uE850';         // Battery0..9 = E850..E859
    private const char Battery10 = '\uE83F';
    private const char BatteryCharging0 = '\uE85A'; // BatteryCharging0..8 = E85A..E862
    private const char BatteryCharging9 = '\uE83E';

    /// <summary>Maps a status to the indicator; null means the read failed.</summary>
    public static TrayIndicator From(BatteryStatus? status)
    {
        if (status is not { Connection: ConnectionState.Connected, Percent: { } percent, Level: { } level })
            return new TrayIndicator(Unknown, IndicatorTint.Theme);

        var n = (Math.Clamp(percent, 0, 100) + 5) / 10; // 0..10
        var glyph = status.Charging == ChargingState.Charging
            // shortcut: BatteryCharging10 (EA93?) unverified, cap at 9; check on Windows
            ? (n >= 9 ? BatteryCharging9 : (char)(BatteryCharging0 + n))
            : (n >= 10 ? Battery10 : (char)(Battery0 + n));

        var tint = level switch
        {
            BatteryLevel.Good => IndicatorTint.Theme,
            BatteryLevel.Low => IndicatorTint.Orange,
            _ => IndicatorTint.Red,
        };
        return new TrayIndicator(glyph, tint);
    }
}
