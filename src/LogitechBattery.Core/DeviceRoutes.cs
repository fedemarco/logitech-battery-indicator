using LogitechBattery.Battery;
using LogitechBattery.HidPp;

namespace LogitechBattery;

/// <summary>
/// The devices that answer now (their routes) and every device seen this session. Changed only under the provider's read lock;
/// the routes are an immutable array that report threads may read at any time.
/// </summary>
internal sealed class DeviceRoutes
{
    private enum ChannelPass { NoDeviceAnswered, DeviceAnswered, ChannelFailed }

    private enum SlotState { Empty, Occupied }

    private volatile BatteryTarget[] _routes = [];
    // Replaced, never changed, so reader threads can look up a name at any time. Kept when routes are dropped: the device at a slot stays the same.
    private volatile IReadOnlyDictionary<Slot, string> _deviceNamesBySlot = new Dictionary<Slot, string>();
    private readonly HashSet<Slot> _slotsWithoutBattery = [];
    private readonly HashSet<Slot> _slotsOfRoutedDevices = [];

    public KnownDevices Known { get; } = new();

    public IReadOnlyList<BatteryTarget> All => _routes;

    /// <summary>The name of the last device found at the slot, even when it does not answer now; null when none was found.</summary>
    public string? NameAt(Slot slot) => _deviceNamesBySlot.GetValueOrDefault(slot);

    public bool IsBatteryEventSource(IHidPpChannel channel, byte[] report) =>
        _routes.Any(route => route.Channel == channel && HidPpMessage.IsEventFrom(report, route.DeviceIndex, route.FeatureIndex));

    /// <summary>Forgets all routes, so the next discovery looks at every slot again.</summary>
    public void Clear()
    {
        _routes = [];
        _slotsOfRoutedDevices.Clear();
    }

    /// <summary>
    /// Probes slots without Unified Battery again. Called only after a USB change or a resume, not at every rediscovery
    /// (which a device that cannot be opened triggers at every read), so a skipped slot is logged and probed once per such event.
    /// shortcut: a different device paired into a skipped slot is missed until the next USB change or resume.
    /// </summary>
    public void ForgetSkippedSlots() => _slotsWithoutBattery.Clear();

    public void Drop(BatteryTarget route) => KeepRoutes(other => other != route);

    public void DropRoutesOn(IHidPpChannel channel) => KeepRoutes(route => route.Channel != channel);

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

    /// <summary>Probes every slot that has no route yet. Returns the channels that failed (e.g. unplugged) while doing so.</summary>
    public IReadOnlyList<IHidPpChannel> Discover(IEnumerable<IHidPpChannel> channels, CancellationToken cancellationToken)
    {
        var failedChannels = new List<IHidPpChannel>();
        var channelsWithoutDirectDevice = new List<IHidPpChannel>();

        // FF is tried on every channel before any receiver slot, so the cable route wins over a receiver route to the same mouse.
        foreach (var channel in channels.Where(channel => !channel.IsClosed))
        {
            switch (DiscoverOn(channel, HidPpMessage.CableDeviceIndexes, cancellationToken))
            {
                case ChannelPass.NoDeviceAnswered: channelsWithoutDirectDevice.Add(channel); break;
                case ChannelPass.ChannelFailed: failedChannels.Add(channel); break;
            }
        }

        // Over the cable the mouse answers on every index (FF and 01 to 06) with the same data: a device on FF is the only one
        // on that channel, so the receiver slots are not probed there (they would find the same mouse six times).
        foreach (var channel in channelsWithoutDirectDevice)
        {
            if (DiscoverOn(channel, HidPpMessage.ReceiverDeviceIndexes, cancellationToken) == ChannelPass.ChannelFailed)
                failedChannels.Add(channel);
        }
        return failedChannels;
    }

    private ChannelPass DiscoverOn(IHidPpChannel channel, IEnumerable<byte> deviceIndexes, CancellationToken cancellationToken)
    {
        try
        {
            var occupiedSlots = 0;
            foreach (var deviceIndex in deviceIndexes)
            {
                if (DiscoverSlot(new Slot(channel.Key, deviceIndex), channel, cancellationToken) == SlotState.Occupied)
                    occupiedSlots++;
            }
            return occupiedSlots > 0 ? ChannelPass.DeviceAnswered : ChannelPass.NoDeviceAnswered;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException)
        {
            return ChannelPass.ChannelFailed;
        }
    }

    private SlotState DiscoverSlot(Slot slot, IHidPpChannel channel, CancellationToken cancellationToken)
    {
        if (IsAlreadyKnown(slot))
            return SlotState.Occupied;

        switch (ProbeOrSkip(slot, channel, cancellationToken))
        {
            case SlotProbe.Found found:
                Add(found.Target);
                return SlotState.Occupied;
            case SlotProbe.NoUnifiedBattery skipped:
                _slotsWithoutBattery.Add(slot);
                AppLog.Warn("Skipping device", ("device", skipped.DeviceName), ("reason", "noUnifiedBattery"));
                return SlotState.Occupied;
            default:
                return SlotState.Empty;
        }
    }

    // One misbehaving device must not stop the others: it is logged once and not probed again until ForgetSkippedSlots.
    private SlotProbe ProbeOrSkip(Slot slot, IHidPpChannel channel, CancellationToken cancellationToken)
    {
        try
        {
            return DeviceDiscovery.Probe(channel, slot.DeviceIndex, cancellationToken);
        }
        catch (Exception ex) when (ex is not (IOException or TimeoutException or OperationCanceledException))
        {
            AppLog.Warn("Skipping device", [("receiver", HidPpDevices.LogId(slot.ChannelKey)), ("devIdx", $"{slot.DeviceIndex:X2}"), ("reason", "probingFailed"), .. AppLog.FailureWithStack(ex)]);
            _slotsWithoutBattery.Add(slot);
            return SlotProbe.NoDevice;
        }
    }

    private bool IsAlreadyKnown(Slot slot) =>
        _routes.Any(route => route.Slot == slot) || _slotsWithoutBattery.Contains(slot) || _slotsOfRoutedDevices.Contains(slot);

    private void Add(BatteryTarget target)
    {
        RememberName(target); // also for a second route to a routed device: its link events name the device too
        var routeToSameDevice = _routes.FirstOrDefault(route => route.Device.Id == target.Device.Id);
        if (routeToSameDevice is null)
        {
            SeenAt(target);
            _routes = [.. _routes, target];
        }
        else if (IsOnCable(target) && !IsOnCable(routeToSameDevice))
        {
            // A routed receiver slot is not probed again, so a cable plugged in later wins here, by replacement.
            SeenAt(target); // the device moved to the cable: its "not answering" state now follows the cable
            _slotsOfRoutedDevices.Add(routeToSameDevice.Slot);
            _routes = [.. _routes.Where(route => route != routeToSameDevice), target];
        }
        else
        {
            _slotsOfRoutedDevices.Add(target.Slot); // the same device on a second route: the first one found stays
        }
    }

    private void SeenAt(BatteryTarget target)
    {
        if (Known.Seen(target.Device, target.Slot))
            LogDeviceFound(target);
    }

    private static bool IsOnCable(BatteryTarget target) => HidPpMessage.CableDeviceIndexes.Contains(target.DeviceIndex);

    private void RememberName(BatteryTarget target) =>
        _deviceNamesBySlot = new Dictionary<Slot, string>(_deviceNamesBySlot) { [target.Slot] = target.Device.Name };

    private static void LogDeviceFound(BatteryTarget target)
    {
        AppLog.Info("Device found", ("device", target.Device.Name), ("pid", $"{target.Channel.ProductId:X4}"),
            ("devIdx", $"{target.DeviceIndex:X2}"), ("featureIndex", $"{target.FeatureIndex:X2}"));
        if (target.Device.Id.IsRouteFallback)
            AppLog.Info("Device has no unit ID, identity follows the slot", ("device", target.Device.Name));
    }
}
