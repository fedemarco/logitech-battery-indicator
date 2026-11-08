using LogitechBattery.Battery;

namespace LogitechBattery.HidPp;

/// <summary>HID++ 2.0 feature 0x1004 (Unified Battery).</summary>
internal static class UnifiedBattery
{
    public const ushort FeatureId = 0x1004;
    public const int GetCapabilitiesFunction = 0;
    public const int GetStatusFunction = 1;

    private const int StateOfChargeIndex = 0;
    private const int ChargingStatusIndex = 2;
    private const int StatusLength = 4;

    private const byte Discharging = 0;
    private const byte Charging = 1;
    private const byte ChargingSlow = 2;
    private const byte ChargeComplete = 3;

    /// <summary>Parses getStatus params: [stateOfCharge%, levelFlags, chargingStatus, externalPower]. Throws FormatException if shorter.</summary>
    public static BatteryStatus ParseStatus(ReadOnlySpan<byte> parameters, DateTimeOffset readAt)
    {
        if (parameters.Length < StatusLength)
            throw new FormatException($"Unified Battery status needs {StatusLength} bytes, got {parameters.Length}.");
        var charging = parameters[ChargingStatusIndex] switch
        {
            Discharging => ChargingState.Discharging,
            Charging or ChargingSlow => ChargingState.Charging,
            ChargeComplete => ChargingState.Full,
            _ => ChargingState.Unknown,
        };
        var percent = Math.Clamp((int)parameters[StateOfChargeIndex], 0, BatteryStatus.FullPercent);
        return new BatteryStatus(ConnectionState.Connected, percent, charging, readAt);
    }
}
