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

    /// <summary>Report id, device index, feature index, function and software id.</summary>
    public const int HeaderLength = 4;
    public const int ParamsOffset = HeaderLength;

    /// <summary>Identifies our requests in the answers (low nibble of the function byte).</summary>
    public const byte SoftwareId = 0x0A;

    /// <summary>Device index used on the USB cable.</summary>
    public static readonly byte[] CableDeviceIndexes = [0xFF];

    /// <summary>Device indexes of the paired devices on a Lightspeed receiver.</summary>
    public static readonly byte[] ReceiverDeviceIndexes = [1, 2, 3, 4, 5, 6];

    public const byte RootFeatureIndex = 0;
    public const int GetFeatureFunction = 0;
    public const int PingFunction = 1;
    public const byte PingEcho = 0x5A;

    private const int DeviceIndexOffset = 1;
    private const int FeatureIndexOffset = 2;
    private const int FunctionOffset = 3;
    private const int MinReplyLength = 6;

    // Error reports carry the failed request after the marker: [id, dev, marker, feature, fn|sw, code].
    private const int ErrorMarkerOffset = 2;
    private const int ErrorFeatureOffset = 3;
    private const int ErrorFunctionOffset = 4;
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
        report[0] = LongId;
        report[DeviceIndexOffset] = deviceIndex;
        report[FeatureIndexOffset] = featureIndex;
        report[FunctionOffset] = FunctionAndSoftwareId(function, softwareId);
        parameters.CopyTo(report, ParamsOffset);
        return report;
    }

    /// <summary>Root feature, getProtocolVersion used as a ping: a HID++ 2.0 device echoes <see cref="PingEcho"/>.</summary>
    public static byte[] BuildPing(byte deviceIndex) =>
        BuildRequest(deviceIndex, RootFeatureIndex, PingFunction, SoftwareId, 0, 0, PingEcho);

    /// <summary>Root feature, getFeature: asks which feature index the device uses for <paramref name="featureId"/>.</summary>
    public static byte[] BuildGetFeature(byte deviceIndex, ushort featureId) =>
        BuildRequest(deviceIndex, RootFeatureIndex, GetFeatureFunction, SoftwareId, (byte)(featureId >> 8), (byte)featureId);

    /// <summary>The parameter bytes of a report (up to the 20-byte long report size).</summary>
    public static byte[] ParamsOf(byte[] report) => report[ParamsOffset..Math.Min(report.Length, LongLength)];

    /// <summary>Tells whether <paramref name="report"/> answers <paramref name="request"/>.</summary>
    public static HidPpReply Classify(byte[] request, byte[] report)
    {
        if (request.Length < HeaderLength || report.Length < MinReplyLength || !IsFromSameDevice(request, report))
            return NotAnAnswer;
        if (report[0] is not (ShortId or LongId))
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

    private static byte FunctionAndSoftwareId(int function, byte softwareId) => (byte)((function << 4) | (softwareId & 0x0F));

    private static bool IsFromSameDevice(byte[] request, byte[] report) => report[DeviceIndexOffset] == request[DeviceIndexOffset];

    private static bool IsResponse(byte[] request, byte[] report) =>
        report[FeatureIndexOffset] == request[FeatureIndexOffset] && report[FunctionOffset] == request[FunctionOffset];

    private static bool IsError20(byte[] request, byte[] report) =>
        report[ErrorMarkerOffset] == Error20Marker && IsErrorFor(request, report);

    private static bool IsError10(byte[] request, byte[] report) =>
        report[0] == ShortId && report[ErrorMarkerOffset] == Error10Marker && IsErrorFor(request, report);

    private static bool IsErrorFor(byte[] request, byte[] report) =>
        report[ErrorFeatureOffset] == request[FeatureIndexOffset] && report[ErrorFunctionOffset] == request[FunctionOffset];
}
