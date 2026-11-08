namespace LogitechBattery.HidPp;

/// <summary>A paired device's link to the receiver changed: it connected (<see cref="LinkEstablished"/>) or went away.</summary>
internal readonly record struct DeviceConnection(byte DeviceIndex, bool LinkEstablished);

/// <summary>HID++ 1.0 receiver notifications: the flags register that switches them on, and the device connection notification.</summary>
internal static class ReceiverNotifications
{
    public const byte FlagsRegister = 0x00;
    private const int FlagsLength = 3;

    // "Wireless" notifications (device connected / disconnected) are flag 0x000100: bit 0 of the middle byte.
    private const int WirelessFlagByte = 1;
    private const byte WirelessFlagBit = 0x01;

    // Device connection notification: [0x10, devIdx, 0x41, protocol, flags, wpid lo, wpid hi].
    private const byte ConnectionSubId = 0x41;
    private const int SubIdOffset = 2;
    private const int ConnectionFlagsOffset = 4;
    private const byte LinkNotEstablishedBit = 0x40;

    /// <summary>
    /// The same flags with wireless notifications on; every other flag is kept. False when nothing needs writing:
    /// the flag is already on, or the receiver answered with fewer than <see cref="FlagsLength"/> flag bytes.
    /// </summary>
    public static bool TryEnableWireless(ReadOnlySpan<byte> flags, out byte[] updated)
    {
        updated = [];
        if (flags.Length < FlagsLength || (flags[WirelessFlagByte] & WirelessFlagBit) != 0)
            return false;

        updated = flags[..FlagsLength].ToArray();
        updated[WirelessFlagByte] |= WirelessFlagBit;
        return true;
    }

    /// <summary>Recognizes the receiver's device connection notification (sent when the mouse wakes, sleeps or is switched on or off); null for any other report.</summary>
    public static DeviceConnection? ReadConnection(byte[] report)
    {
        var isConnection = report.Length >= HidPpMessage.ShortLength
            && report[HidPpMessage.ReportIdOffset] == HidPpMessage.ShortId
            && report[SubIdOffset] == ConnectionSubId;
        if (!isConnection)
            return null;

        var linkEstablished = (report[ConnectionFlagsOffset] & LinkNotEstablishedBit) == 0;
        return new DeviceConnection(report[HidPpMessage.DeviceIndexOffset], linkEstablished);
    }
}
