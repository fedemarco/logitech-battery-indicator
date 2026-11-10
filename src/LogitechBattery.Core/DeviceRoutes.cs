using LogitechBattery.Battery;
using LogitechBattery.HidPp;

namespace LogitechBattery;

/// <summary>
/// The devices that answer now (their routes) and every device seen this session. Changed only under the provider's read lock;
/// the routes are an immutable array that report threads may read at any time.
/// </summary>
internal sealed class DeviceRoutes
{
    private volatile BatteryTarget[] _routes = [];
    private readonly HashSet<(string ChannelKey, byte DeviceIndex)> _slotsWithoutBattery = [];
    private readonly HashSet<(string ChannelKey, byte DeviceIndex)> _slotsOfRoutedDevices = [];

    public KnownDevices Known { get; } = new();

    public IReadOnlyList<BatteryTarget> All => _routes;

    public bool IsBatteryEventSource(HidPpChannel channel, byte[] report) =>
        _routes.Any(route => route.Channel == channel && HidPpMessage.IsEventFrom(report, route.DeviceIndex, route.FeatureIndex));

    /// <summary>Forgets all routes and skipped slots: the next discovery looks at every slot again.</summary>
    public void Clear()
    {
        _routes = [];
        _slotsWithoutBattery.Clear();
        _slotsOfRoutedDevices.Clear();
    }

    public void Drop(BatteryTarget route) => KeepRoutes(other => other != route);

    public void DropRoutesOn(HidPpChannel channel) => KeepRoutes(route => route.Channel != channel);

    public void DropRoutesOnClosedChannels() => KeepRoutes(route => !route.Channel.IsClosed);

    // A dropped route may free the way for a second route to the same device, so those slots are probed again.
    private void KeepRoutes(Func<BatteryTarget, bool> keep)
    {
        var kept = _routes.Where(keep).ToArray();
        if (kept.Length == _routes.Length)
            return;
        _routes = kept;
        _slotsOfRoutedDevices.Clear();
    }

    /// <summary>Probes every index that has no route yet. Returns the channels that failed (e.g. unplugged) while doing so.</summary>
    public IReadOnlyList<HidPpChannel> Discover(IEnumerable<HidPpChannel> channels, CancellationToken cancellationToken)
    {
        var failedChannels = new List<HidPpChannel>();
        var receiverCandidates = new List<HidPpChannel>();
        foreach (var channel in channels.Where(channel => !channel.IsClosed))
        {
            // Over the cable the mouse answers on every index (FF and 01 to 06) with the same data: a device on FF is the only one
            // on that channel, so the receiver slots are not probed there (they would find the same mouse six times).
            // FF is tried on every channel before any receiver slot, so the cable route wins over a receiver route to the same mouse.
            if (!TryDiscover(channel, HidPpMessage.CableDeviceIndexes, cancellationToken, failedChannels) && !failedChannels.Contains(channel))
                receiverCandidates.Add(channel);
        }
        foreach (var channel in receiverCandidates)
            TryDiscover(channel, HidPpMessage.ReceiverDeviceIndexes, cancellationToken, failedChannels);
        return failedChannels;
    }

    /// <summary>True if a device answers on one of the indexes.</summary>
    private bool TryDiscover(HidPpChannel channel, IEnumerable<byte> deviceIndexes, CancellationToken cancellationToken, List<HidPpChannel> failedChannels)
    {
        try
        {
            var anyDevice = false;
            foreach (var deviceIndex in deviceIndexes)
                anyDevice |= DiscoverIndex(channel, deviceIndex, cancellationToken);
            return anyDevice;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException)
        {
            failedChannels.Add(channel);
            return false;
        }
    }

    /// <summary>True if a device answers at this index (also when it is routed already or has no Unified Battery).</summary>
    private bool DiscoverIndex(HidPpChannel channel, byte deviceIndex, CancellationToken cancellationToken)
    {
        var slot = (channel.Key, deviceIndex);
        if (HasRoute(channel, deviceIndex) || _slotsWithoutBattery.Contains(slot) || _slotsOfRoutedDevices.Contains(slot))
            return true;

        switch (DeviceDiscovery.Probe(channel, deviceIndex, cancellationToken))
        {
            case SlotProbe.Found found:
                Add(found.Target, slot);
                return true;
            case SlotProbe.NoUnifiedBattery skipped:
                // shortcut: a slot without Unified Battery is not probed again until the next rediscovery (USB change, closed channel, resume); a re-pair without one of those is missed until then.
                _slotsWithoutBattery.Add(slot);
                AppLog.Write($"Skipping device: {skipped.DeviceName}, no Unified Battery");
                return true;
            default:
                return false;
        }
    }

    private bool HasRoute(HidPpChannel channel, byte deviceIndex) =>
        _routes.Any(route => route.Channel == channel && route.DeviceIndex == deviceIndex);

    private void Add(BatteryTarget target, (string ChannelKey, byte DeviceIndex) slot)
    {
        if (_routes.Any(route => route.Device.Id == target.Device.Id))
        {
            _slotsOfRoutedDevices.Add(slot); // the same device on a second route: the first one found (the cable) stays
            return;
        }

        AppLog.Write($"Device found: {target.Device.Name}, PID {target.Channel.ProductId:X4}, devIdx {target.DeviceIndex:X2}, battery feature index {target.FeatureIndex:X2}");
        _routes = [.. _routes, target];
        Known.Seen(target.Device, target.Channel.Key);
    }
}
