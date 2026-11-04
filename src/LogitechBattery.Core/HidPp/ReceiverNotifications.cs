namespace LogitechBattery.HidPp;

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
    private const byte LinkNotEstablished = 0x40;

    public static bool HasWireless(ReadOnlySpan<byte> flags) => (flags[WirelessFlagByte] & WirelessFlagBit) != 0;

    /// <summary>The same flags with wireless notifications on; every other flag is kept.</summary>
    public static byte[] WithWireless(ReadOnlySpan<byte> flags)
    {
        var updated = flags[..FlagsLength].ToArray();
        updated[WirelessFlagByte] |= WirelessFlagBit;
        return updated;
    }

    /// <summary>Recognizes the receiver's device connection notification (sent when the mouse wakes, sleeps or is switched on or off).</summary>
    public static bool TryReadConnection(byte[] report, out byte deviceIndex, out bool linkEstablished)
    {
        var isConnection = report.Length >= HidPpMessage.ShortLength
            && report[0] == HidPpMessage.ShortId
            && report[SubIdOffset] == ConnectionSubId;
        deviceIndex = isConnection ? report[1] : (byte)0;
        linkEstablished = isConnection && (report[ConnectionFlagsOffset] & LinkNotEstablished) == 0;
        return isConnection;
    }
}
