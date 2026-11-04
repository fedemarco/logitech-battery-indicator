using LogitechBattery.Battery;

namespace LogitechBattery.HidPp;

/// <summary>HID++ 2.0 feature 0x1004 (Unified Battery).</summary>
internal static class UnifiedBattery
{
    public const ushort FeatureId = 0x1004;
    public const int GetStatusFunction = 1;

    /// <summary>Parses getStatus params: [stateOfCharge%, levelFlags, chargingStatus, externalPower].</summary>
    public static BatteryStatus ParseStatus(ReadOnlySpan<byte> p, DateTimeOffset readAt)
    {
        var charging = p[2] switch
        {
            0 => ChargingState.Discharging,
            1 or 2 => ChargingState.Charging,
            3 => ChargingState.Full,
            _ => ChargingState.Unknown,
        };
        return new BatteryStatus(ConnectionState.Connected, Math.Clamp((int)p[0], 0, 100), charging, readAt);
    }
}
