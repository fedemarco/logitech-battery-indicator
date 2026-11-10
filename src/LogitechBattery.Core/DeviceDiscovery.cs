using LogitechBattery.Battery;
using LogitechBattery.HidPp;

namespace LogitechBattery;

/// <summary>A device with Unified Battery found by discovery: the channel it answers on, its device and battery feature index, and who it is.</summary>
internal sealed record BatteryTarget(HidPpChannel Channel, byte DeviceIndex, byte FeatureIndex, DeviceInfo Device)
{
    public Slot Slot => new(Channel.Key, DeviceIndex);
}

/// <summary>What answers at one device index of a channel.</summary>
internal abstract record SlotProbe
{
    public static readonly SlotProbe NoDevice = new NoDeviceProbe();

    private sealed record NoDeviceProbe : SlotProbe;

    /// <summary>A HID++ 2.0 device that has no Unified Battery (for example a keyboard with another battery feature).</summary>
    public sealed record NoUnifiedBattery(string DeviceName) : SlotProbe;

    public sealed record Found(BatteryTarget Target) : SlotProbe;
}

/// <summary>Asks one device index of a channel who is there and whether it reports its battery.</summary>
internal static class DeviceDiscovery
{
    /// <summary>How long a quick one-shot request (discovery, receiver flags) may wait for its answer; a sleeping device does not answer.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(500);

    private const string UnnamedDeviceName = "Logitech device";

    private enum LookupOutcome { Unanswered, NotSupported, Found }

    /// <summary>
    /// A lookup that can fail in two ways: the device did not answer (maybe only now; ask again next time) or it does not have the feature.
    /// Identity falls back (route id, generic name) only for the second, so a passing failure never creates a phantom device.
    /// </summary>
    private readonly record struct Lookup<T>(LookupOutcome Outcome, T? Value = default)
    {
        public static Lookup<T> Of(T value) => new(LookupOutcome.Found, value);

        public bool IsFound => Outcome == LookupOutcome.Found;

        public Lookup<TOther> WithoutValue<TOther>() => new(Outcome);
    }

    /// <summary>Throws IOException or TimeoutException when the channel itself fails.</summary>
    public static SlotProbe Probe(HidPpChannel channel, byte deviceIndex, CancellationToken cancellationToken)
    {
        if (RequestDuringDiscovery(channel, HidPpMessage.BuildPing(deviceIndex), cancellationToken) is null)
            return SlotProbe.NoDevice;

        var battery = FindFeatureIndex(channel, deviceIndex, UnifiedBattery.FeatureId, cancellationToken);
        if (battery.Outcome == LookupOutcome.Unanswered)
            return SlotProbe.NoDevice;

        var name = ReadName(channel, deviceIndex, cancellationToken);
        if (battery.Outcome == LookupOutcome.NotSupported)
            return new SlotProbe.NoUnifiedBattery(name.IsFound ? name.Value! : UnnamedDeviceName);
        if (name.Outcome == LookupOutcome.Unanswered)
            return SlotProbe.NoDevice;

        var deviceName = name.IsFound ? name.Value! : UnnamedDeviceName;
        var id = ReadId(channel, deviceIndex, cancellationToken);
        if (id.Outcome == LookupOutcome.Unanswered)
            return SlotProbe.NoDevice;

        var device = new DeviceInfo(id.Value!, deviceName);
        return new SlotProbe.Found(new BatteryTarget(channel, deviceIndex, battery.Value, device));
    }

    /// <summary>
    /// The unit ID (feature 0x0003). The route fallback when the device has no such feature, answers with an error,
    /// or reports an invalid ID: those answers do not change on the next read. Only a missing answer or a Busy error is retried.
    /// </summary>
    private static Lookup<DeviceId> ReadId(HidPpChannel channel, byte deviceIndex, CancellationToken cancellationToken)
    {
        var routeFallback = Lookup<DeviceId>.Of(DeviceId.FromRoute(channel.ProductId, deviceIndex));
        var feature = FindFeatureIndex(channel, deviceIndex, DeviceInformation.FeatureId, cancellationToken);
        if (feature.Outcome == LookupOutcome.NotSupported)
            return routeFallback;
        if (!feature.IsFound)
            return feature.WithoutValue<DeviceId>();

        var request = HidPpMessage.BuildRequest(deviceIndex, feature.Value, DeviceInformation.GetDeviceInfoFunction, HidPpMessage.SoftwareId);
        var answer = channel.Exchange(request, Timeout, cancellationToken);
        if (answer.Response is not { } response)
            return IsTransient(answer.ErrorCode) ? new Lookup<DeviceId>(LookupOutcome.Unanswered) : routeFallback;

        return DeviceId.TryParse(DeviceInformation.UnitIdOf(HidPpMessage.ParamsOf(response)), out var unitId)
            ? Lookup<DeviceId>.Of(unitId)
            : routeFallback;
    }

    private static bool IsTransient(byte? errorCode) => errorCode is null or DeviceInformation.BusyErrorCode;

    /// <summary>Asks the root feature for the index of <paramref name="featureId"/>.</summary>
    private static Lookup<byte> FindFeatureIndex(HidPpChannel channel, byte deviceIndex, ushort featureId, CancellationToken cancellationToken)
    {
        var response = RequestDuringDiscovery(channel, HidPpMessage.BuildGetFeature(deviceIndex, featureId), cancellationToken);
        if (response is null)
            return new Lookup<byte>(LookupOutcome.Unanswered);

        var featureIndex = FeatureIndexOf(response);
        return featureIndex == 0 ? new Lookup<byte>(LookupOutcome.NotSupported) : Lookup<byte>.Of(featureIndex);
    }

    /// <summary>Reads the DeviceName feature (0x0005); an empty name counts as not supported.</summary>
    private static Lookup<string> ReadName(HidPpChannel channel, byte deviceIndex, CancellationToken cancellationToken)
    {
        var feature = FindFeatureIndex(channel, deviceIndex, DeviceName.FeatureId, cancellationToken);
        if (!feature.IsFound)
            return feature.WithoutValue<string>();

        var countRequest = HidPpMessage.BuildRequest(deviceIndex, feature.Value, DeviceName.GetCountFunction, HidPpMessage.SoftwareId);
        if (RequestDuringDiscovery(channel, countRequest, cancellationToken) is not { } countResponse)
            return new Lookup<string>(LookupOutcome.Unanswered);
        if (NameLengthOf(countResponse) == 0)
            return new Lookup<string>(LookupOutcome.NotSupported);

        var length = DeviceName.LimitLength(NameLengthOf(countResponse));
        var chunks = DeviceName.ReadChunks(length, position =>
        {
            var request = HidPpMessage.BuildRequest(deviceIndex, feature.Value, DeviceName.GetNameFunction, HidPpMessage.SoftwareId, position);
            return RequestDuringDiscovery(channel, request, cancellationToken) is { } chunk ? HidPpMessage.ParamsOf(chunk) : null;
        });
        return DeviceName.IsComplete(length, chunks)
            ? Lookup<string>.Of(DeviceName.Assemble(length, chunks))
            : new Lookup<string>(LookupOutcome.Unanswered);
    }

    // The first parameter byte means something different in each response.
    private static byte FeatureIndexOf(byte[] getFeatureResponse) => HidPpMessage.ParamsOf(getFeatureResponse)[0];

    private static byte NameLengthOf(byte[] getNameCountResponse) => HidPpMessage.ParamsOf(getNameCountResponse)[0];

    private static byte[]? RequestDuringDiscovery(HidPpChannel channel, byte[] request, CancellationToken cancellationToken) =>
        channel.Exchange(request, Timeout, cancellationToken).Response;
}
