using LogitechBattery.HidPp;

namespace LogitechBattery;

/// <summary>The mouse found by discovery: the channel it answers on, and its device and Unified Battery feature index.</summary>
internal sealed record BatteryTarget(HidPpChannel Channel, byte DeviceIndex, byte FeatureIndex);

/// <summary>Finds the mouse among the open HID++ channels: first the cable's device index, then the receiver slots.</summary>
internal static class DeviceDiscovery
{
    /// <summary>How long a discovery request may wait for its answer (a sleeping device does not answer).</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(500);

    // shortcut: single supported model until device selection (multiple devices) is implemented
    private const string SupportedDeviceName = "PRO X2 SUPERSTRIKE";

    /// <summary>Pass 1: the cable's device index; pass 2: the receiver slots. First HID++ 2.0 device with Unified Battery wins.</summary>
    public static BatteryTarget? Discover(IEnumerable<HidPpChannel> channels, CancellationToken cancellationToken)
    {
        foreach (var deviceIndexes in new[] { HidPpMessage.CableDeviceIndexes, HidPpMessage.ReceiverDeviceIndexes })
        {
            foreach (var channel in channels.Where(channel => !channel.IsClosed))
            {
                if (FindTarget(channel, deviceIndexes, cancellationToken) is { } target)
                    return target;
            }
        }
        return null;
    }

    private static BatteryTarget? FindTarget(HidPpChannel channel, IEnumerable<byte> deviceIndexes, CancellationToken cancellationToken)
    {
        foreach (var deviceIndex in deviceIndexes)
        {
            try
            {
                if (FindBatteryFeatureIndex(channel, deviceIndex, cancellationToken) is not { } featureIndex)
                    continue;

                var name = ReadName(channel, deviceIndex, cancellationToken);
                if (name != SupportedDeviceName)
                {
                    AppLog.Write($"Skipping device: PID {channel.ProductId:X4}, devIdx {deviceIndex:X2}, name {(name is null ? "unreadable" : $"\"{name}\"")}");
                    continue;
                }
                AppLog.Write($"Device found: PID {channel.ProductId:X4}, devIdx {deviceIndex:X2}, battery feature index {featureIndex:X2}");
                return new BatteryTarget(channel, deviceIndex, featureIndex);
            }
            catch (Exception ex) when (ex is IOException or TimeoutException)
            {
                break; // the channel itself failed (e.g. unplugged): the remaining indexes would fail the same way
            }
        }
        return null;
    }

    private static byte? FindBatteryFeatureIndex(HidPpChannel channel, byte deviceIndex, CancellationToken cancellationToken)
    {
        if (RequestDuringDiscovery(channel, HidPpMessage.BuildPing(deviceIndex), cancellationToken) is null)
            return null;
        return FindFeatureIndex(channel, deviceIndex, UnifiedBattery.FeatureId, cancellationToken);
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
