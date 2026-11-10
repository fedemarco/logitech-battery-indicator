namespace LogitechBattery.HidPp;

/// <summary>HID++ 2.0 feature 0x0003 (DeviceInformation).</summary>
internal static class DeviceInformation
{
    public const ushort FeatureId = 0x0003;
    public const int GetDeviceInfoFunction = 0;

    /// <summary>HID++ 2.0 error code "Busy": the device cannot answer right now, but will later. Every other error is permanent.</summary>
    public const byte BusyErrorCode = 0x08;

    // Layout from the protocol (getDeviceInfo: entity count, unit ID, transport, model ID, ...).
    // Not yet confirmed on this mouse, so the probe also logs the raw report.
    private const int UnitIdOffset = 1;
    private const int UnitIdLength = 4;

    /// <summary>Returns the unit ID of getDeviceInfo params as 8 uppercase hex characters.</summary>
    public static string UnitIdOf(ReadOnlySpan<byte> parameters) =>
        Convert.ToHexString(parameters.Slice(UnitIdOffset, UnitIdLength));
}
