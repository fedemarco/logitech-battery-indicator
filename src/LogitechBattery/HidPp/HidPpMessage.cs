namespace LogitechBattery.HidPp;

internal enum HidPpReplyKind
{
    /// <summary>Not an answer to the request (notification, other device, other request).</summary>
    None,
    Response,
    /// <summary>HID++ 2.0 error: [0x11, dev, 0xFF, feature, fn|sw, code].</summary>
    Error20,
    /// <summary>HID++ 1.0 error: [0x10, dev, 0x8F, subId, address, code].</summary>
    Error10,
}

internal readonly record struct HidPpReply(HidPpReplyKind Kind, byte ErrorCode = 0);

/// <summary>Pure HID++ 2.0 message helpers (no I/O).</summary>
internal static class HidPpMessage
{
    public const int LongLength = 20;
    public const byte ShortId = 0x10;
    public const byte LongId = 0x11;

    /// <summary>Builds a long HID++ 2.0 request, zero-padded to 20 bytes.</summary>
    public static byte[] BuildRequest(byte deviceIndex, byte featureIndex, int function, byte swId, params byte[] parameters)
    {
        if (parameters.Length > LongLength - 4)
            throw new ArgumentException("Too many parameters.", nameof(parameters));
        var report = new byte[LongLength];
        report[0] = LongId;
        report[1] = deviceIndex;
        report[2] = featureIndex;
        report[3] = (byte)((function << 4) | (swId & 0x0F));
        parameters.CopyTo(report, 4);
        return report;
    }

    /// <summary>Tells whether <paramref name="report"/> answers <paramref name="request"/>.</summary>
    public static HidPpReply Classify(byte[] request, byte[] report)
    {
        if (request.Length < 4 || report.Length < 6 || report[1] != request[1])
            return default;
        if (report[0] is not (ShortId or LongId))
            return default;

        if (report[2] == request[2] && report[3] == request[3])
            return new HidPpReply(HidPpReplyKind.Response);
        if (report[2] == 0xFF && report[3] == request[2] && report[4] == request[3])
            return new HidPpReply(HidPpReplyKind.Error20, report[5]);
        if (report[0] == ShortId && report[2] == 0x8F && report[3] == request[2] && report[4] == request[3])
            return new HidPpReply(HidPpReplyKind.Error10, report[5]);
        return default;
    }

    public static string ToHex(byte[] bytes) => BitConverter.ToString(bytes).Replace('-', ' ').ToLowerInvariant();
}
