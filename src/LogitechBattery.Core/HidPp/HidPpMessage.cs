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
    public const byte ShortId = 0x10;
    public const byte LongId = 0x11;
    public const int LongLength = 20;
    public const int ShortLength = 7;

    /// <summary>Report id, device index, feature index (or sub id) and function with software id (or register address).</summary>
    public const int HeaderLength = 4;
    public const int ParamsOffset = HeaderLength;

    /// <summary>Software id (low nibble of the function byte) for requests sent outside a channel, as <c>HidPpProbe</c> does. <c>HidPpChannel</c> replaces it with a rotating id, so for channel callers it is a placeholder.</summary>
    public const byte SoftwareId = 0x0A;

    /// <summary>Device index used on the USB cable.</summary>
    public static readonly IReadOnlyList<byte> CableDeviceIndexes = [0xFF];

    /// <summary>Device indexes of the paired devices on a Lightspeed receiver.</summary>
    public static readonly IReadOnlyList<byte> ReceiverDeviceIndexes = [1, 2, 3, 4, 5, 6];

    /// <summary>Device index that addresses the receiver itself.</summary>
    public const byte ReceiverIndex = 0xFF;

    public const byte RootFeatureIndex = 0;
    public const int GetFeatureFunction = 0;
    public const int PingFunction = 1;
    public const byte PingEcho = 0x5A;

    private const byte SoftwareIdMask = 0x0F;
    private const byte SetRegisterSubId = 0x80;
    private const byte GetRegisterSubId = 0x81;

    // Bytes 0 and 1 are the same in both protocols. Byte 2 is the feature index (HID++ 2.0) or the sub id (HID++ 1.0);
    // byte 3 is function and software id (2.0) or the register address (1.0). The names cover both.
    public const int ReportIdOffset = 0;
    public const int DeviceIndexOffset = 1;
    private const int FeatureOrSubIdOffset = 2;
    private const int FunctionOrAddressOffset = 3;
    private const int FunctionShift = 4; // the function is the high nibble of byte 3, the software id the low one

    // An error report is the longest answer to check: [id, dev, marker, feature, fn|sw, code].
    private const int MinReplyLength = 6;

    // Error reports carry the failed request after the marker: [id, dev, marker, feature, fn|sw, code].
    private const int ErrorMarkerOffset = 2;
    private const int ErrorFeatureOffset = 3;
    private const int ErrorFunctionOrAddressOffset = 4;
    private const int ErrorCodeOffset = 5;
    private const byte Error20Marker = 0xFF;
    private const byte Error10Marker = 0x8F;

    private static readonly HidPpReply NotAnAnswer = new(HidPpReplyKind.None);

    /// <summary>Builds a long HID++ 2.0 request, zero-padded to 20 bytes.</summary>
    public static byte[] BuildRequest(byte deviceIndex, byte featureIndex, int function, byte softwareId, params byte[] parameters)
    {
        if (parameters.Length > LongLength - HeaderLength)
            throw new ArgumentException("Too many parameters.", nameof(parameters));
        var report = new byte[LongLength];
        report[ReportIdOffset] = LongId;
        report[DeviceIndexOffset] = deviceIndex;
        report[FeatureOrSubIdOffset] = featureIndex;
        report[FunctionOrAddressOffset] = FunctionAndSoftwareId(function, softwareId);
        parameters.CopyTo(report, ParamsOffset);
        return report;
    }

    /// <summary>A copy of a long request with another software id; a short (HID++ 1.0) request is returned unchanged, as its byte 3 is a register address.</summary>
    public static byte[] WithSoftwareId(byte[] request, byte softwareId)
    {
        if (request[ReportIdOffset] != LongId)
            return request;
        var copy = (byte[])request.Clone();
        copy[FunctionOrAddressOffset] = (byte)((request[FunctionOrAddressOffset] & ~SoftwareIdMask) | (softwareId & SoftwareIdMask));
        return copy;
    }

    /// <summary>Root feature, getProtocolVersion used as a ping: a HID++ 2.0 device echoes <see cref="PingEcho"/>.</summary>
    public static byte[] BuildPing(byte deviceIndex) =>
        BuildRequest(deviceIndex, RootFeatureIndex, PingFunction, SoftwareId, 0, 0, PingEcho);

    /// <summary>Root feature, getFeature: asks which feature index the device uses for <paramref name="featureId"/>.</summary>
    public static byte[] BuildGetFeature(byte deviceIndex, ushort featureId) =>
        BuildRequest(deviceIndex, RootFeatureIndex, GetFeatureFunction, SoftwareId, (byte)(featureId >> 8), (byte)featureId);

    /// <summary>HID++ 1.0 short "get register" request: reads a register without changing it.</summary>
    public static byte[] BuildGetRegister(byte deviceIndex, byte register) =>
        BuildShortRequest(deviceIndex, GetRegisterSubId, register, []);

    /// <summary>HID++ 1.0 short "set register" request: writes up to three value bytes.</summary>
    public static byte[] BuildSetRegister(byte deviceIndex, byte register, ReadOnlySpan<byte> value)
    {
        var report = new byte[ShortLength];
        report[0] = ShortId;
        report[DeviceIndexOffset] = deviceIndex;
        report[FeatureOrSubIdOffset] = SetRegisterSubId;
        report[FunctionOrAddressOffset] = register;
        value.CopyTo(report.AsSpan(ParamsOffset));
        return report;
    }

    /// <summary>True for a short (HID++ 1.0) report or request.</summary>
    public static bool IsShort(byte[] report) => report[ReportIdOffset] == ShortId;

    /// <summary>True for an unsolicited long report (software id 0) from the given device and feature: a notification, not an answer to anybody's request.</summary>
    public static bool IsEventFrom(byte[] report, byte deviceIndex, byte featureIndex) =>
        report.Length >= HeaderLength
        && report[ReportIdOffset] == LongId
        && report[DeviceIndexOffset] == deviceIndex
        && report[FeatureOrSubIdOffset] == featureIndex
        && (report[FunctionOrAddressOffset] & SoftwareIdMask) == 0;

    /// <summary>The parameter bytes of a report (up to the 20-byte long report size).</summary>
    public static byte[] ParamsOf(byte[] report) => report[ParamsOffset..Math.Min(report.Length, LongLength)];

    /// <summary>Tells whether <paramref name="report"/> answers <paramref name="request"/>.</summary>
    public static HidPpReply Classify(byte[] request, byte[] report)
    {
        if (request.Length < HeaderLength || report.Length < MinReplyLength || !IsFromSameDevice(request, report))
            return NotAnAnswer;
        if (report[ReportIdOffset] is not (ShortId or LongId))
            return NotAnAnswer;

        if (IsResponse(request, report))
            return new HidPpReply(HidPpReplyKind.Response);
        if (IsError20(request, report))
            return new HidPpReply(HidPpReplyKind.Error20, report[ErrorCodeOffset]);
        if (IsError10(request, report))
            return new HidPpReply(HidPpReplyKind.Error10, report[ErrorCodeOffset]);
        return NotAnAnswer;
    }

    public static string ToHex(byte[] bytes) => BitConverter.ToString(bytes).Replace('-', ' ').ToLowerInvariant();

    private static byte[] BuildShortRequest(byte deviceIndex, byte subId, byte address, ReadOnlySpan<byte> parameters)
    {
        var report = new byte[ShortLength];
        report[ReportIdOffset] = ShortId;
        report[DeviceIndexOffset] = deviceIndex;
        report[FeatureOrSubIdOffset] = subId;
        report[FunctionOrAddressOffset] = address;
        parameters.CopyTo(report.AsSpan(ParamsOffset));
        return report;
    }

    private static byte FunctionAndSoftwareId(int function, byte softwareId) => (byte)((function << FunctionShift) | (softwareId & SoftwareIdMask));

    private static bool IsFromSameDevice(byte[] request, byte[] report) => report[DeviceIndexOffset] == request[DeviceIndexOffset];

    private static bool IsResponse(byte[] request, byte[] report) =>
        report[FeatureOrSubIdOffset] == request[FeatureOrSubIdOffset] && report[FunctionOrAddressOffset] == request[FunctionOrAddressOffset];

    private static bool IsError20(byte[] request, byte[] report) =>
        report[ErrorMarkerOffset] == Error20Marker && IsErrorFor(request, report);

    private static bool IsError10(byte[] request, byte[] report) =>
        report[ReportIdOffset] == ShortId && report[ErrorMarkerOffset] == Error10Marker && IsErrorFor(request, report);

    private static bool IsErrorFor(byte[] request, byte[] report) =>
        report[ErrorFeatureOffset] == request[FeatureOrSubIdOffset] && report[ErrorFunctionOrAddressOffset] == request[FunctionOrAddressOffset];
}
