using LogitechBattery.Battery;
using LogitechBattery.HidPp;

namespace LogitechBattery;

/// <summary>A device with Unified Battery found by discovery: the channel it answers on, its device and battery feature index, and who it is.</summary>
internal sealed record BatteryTarget(HidPpChannel Channel, byte DeviceIndex, byte FeatureIndex, DeviceInfo Device);

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

    /// <summary>Throws IOException or TimeoutException when the channel itself fails.</summary>
    public static SlotProbe Probe(HidPpChannel channel, byte deviceIndex, CancellationToken cancellationToken)
    {
        if (RequestDuringDiscovery(channel, HidPpMessage.BuildPing(deviceIndex), cancellationToken) is null)
            return SlotProbe.NoDevice;

        var name = ReadName(channel, deviceIndex, cancellationToken) ?? UnnamedDeviceName;
        if (FindFeatureIndex(channel, deviceIndex, UnifiedBattery.FeatureId, cancellationToken) is not { } featureIndex)
            return new SlotProbe.NoUnifiedBattery(name);

        var device = new DeviceInfo(ReadId(channel, deviceIndex, name, cancellationToken), name);
        return new SlotProbe.Found(new BatteryTarget(channel, deviceIndex, featureIndex, device));
    }

    /// <summary>The unit ID (feature 0x0003); the route fallback if the device has none, does not answer or reports an invalid one.</summary>
    private static DeviceId ReadId(HidPpChannel channel, byte deviceIndex, string name, CancellationToken cancellationToken)
    {
        if (ReadUnitId(channel, deviceIndex, cancellationToken) is { } unitId)
            return unitId;

        AppLog.Write($"Device {name} has no unit ID; identity follows the slot");
        return DeviceId.FromRoute(channel.ProductId, deviceIndex);
    }

    private static DeviceId? ReadUnitId(HidPpChannel channel, byte deviceIndex, CancellationToken cancellationToken)
    {
        if (FindFeatureIndex(channel, deviceIndex, DeviceInformation.FeatureId, cancellationToken) is not { } featureIndex)
            return null;

        var request = HidPpMessage.BuildRequest(deviceIndex, featureIndex, DeviceInformation.GetDeviceInfoFunction, HidPpMessage.SoftwareId);
        if (RequestDuringDiscovery(channel, request, cancellationToken) is not { } response)
            return null;

        return DeviceId.TryParse(DeviceInformation.UnitIdOf(HidPpMessage.ParamsOf(response)), out var unitId) ? unitId : null;
    }

    /// <summary>Asks the root feature for the index of <paramref name="featureId"/>; null if unsupported or unanswered.</summary>
    private static byte? FindFeatureIndex(HidPpChannel channel, byte deviceIndex, ushort featureId, CancellationToken cancellationToken)
    {
        var response = RequestDuringDiscovery(channel, HidPpMessage.BuildGetFeature(deviceIndex, featureId), cancellationToken);
        var featureIndex = response is null ? (byte)0 : FeatureIndexOf(response);
        return featureIndex == 0 ? null : featureIndex;
    }

    /// <summary>Reads the DeviceName feature (0x0005); null if unsupported or unreadable.</summary>
    private static string? ReadName(HidPpChannel channel, byte deviceIndex, CancellationToken cancellationToken)
    {
        if (FindFeatureIndex(channel, deviceIndex, DeviceName.FeatureId, cancellationToken) is not { } featureIndex)
            return null;

        var countResponse = RequestDuringDiscovery(channel, HidPpMessage.BuildRequest(deviceIndex, featureIndex, DeviceName.GetCountFunction, HidPpMessage.SoftwareId), cancellationToken);
        if (countResponse is null || NameLengthOf(countResponse) == 0)
            return null;

        var length = DeviceName.LimitLength(NameLengthOf(countResponse));
        var chunks = DeviceName.ReadChunks(length, position =>
        {
            var request = HidPpMessage.BuildRequest(deviceIndex, featureIndex, DeviceName.GetNameFunction, HidPpMessage.SoftwareId, position);
            return RequestDuringDiscovery(channel, request, cancellationToken) is { } chunk ? HidPpMessage.ParamsOf(chunk) : null;
        });
        return DeviceName.IsComplete(length, chunks) ? DeviceName.Assemble(length, chunks) : null;
    }

    // The first parameter byte means something different in each response.
    private static byte FeatureIndexOf(byte[] getFeatureResponse) => HidPpMessage.ParamsOf(getFeatureResponse)[0];

    private static byte NameLengthOf(byte[] getNameCountResponse) => HidPpMessage.ParamsOf(getNameCountResponse)[0];

    private static byte[]? RequestDuringDiscovery(HidPpChannel channel, byte[] request, CancellationToken cancellationToken) =>
        channel.Exchange(request, Timeout, cancellationToken).Response;
}
