using LogitechBattery.HidPp;
using LogitechBattery.Logging;

namespace LogitechBattery.Devices;

/// <summary>The open HID++ channels (receivers and cables): opens new ones, drops closed and lost ones.</summary>
internal sealed class HidPpChannels
{
    private readonly List<HidPpChannel> _channels = [];
    private readonly HashSet<string> _devicesFailedToOpen = [];
    private readonly Action<HidPpChannel, byte[]> _reportHandler;

    public HidPpChannels(Action<HidPpChannel, byte[]> reportHandler) => _reportHandler = reportHandler;

    public IReadOnlyList<HidPpChannel> All => _channels;

    public bool AnyClosed => _channels.Any(channel => channel.IsClosed);

    public bool Contains(IHidPpChannel channel) => _channels.Any(known => known == channel);

    public IReadOnlySet<string> OpenKeys() => _channels.Where(channel => !channel.IsClosed).Select(channel => channel.Key).ToHashSet();

    /// <summary>Drops the closed channels and opens one for every HID++ device that has none. True when a device could not be opened.</summary>
    public bool Refresh(CancellationToken cancellationToken)
    {
        foreach (var closed in _channels.Where(channel => channel.IsClosed).ToList())
        {
            closed.Dispose();
            _channels.Remove(closed);
        }

        var devices = HidPpDevices.Find();
        _devicesFailedToOpen.IntersectWith(devices.Select(device => device.Key)); // an unplugged device is logged again if it fails after a replug

        var anyFailedToOpen = false;
        foreach (var device in devices.Where(device => _channels.All(channel => channel.Key != device.Key)))
        {
            if (!OpenChannel(device, cancellationToken))
                anyFailedToOpen = true;
        }
        return anyFailedToOpen;
    }

    /// <summary>Disposes the channel and forgets it.</summary>
    public void Remove(HidPpChannel channel)
    {
        channel.Dispose();
        _channels.Remove(channel);
    }

    public void CloseAll()
    {
        foreach (var channel in _channels)
            channel.Dispose();
        _channels.Clear();
        _devicesFailedToOpen.Clear(); // after a resume, a device that still cannot be opened is logged again
    }

    /// <summary>True when the channel is open; false when the device could not be opened.</summary>
    private bool OpenChannel(HidPpDevice device, CancellationToken cancellationToken)
    {
        if (HidPpChannel.Open(device, _reportHandler) is not { } channel)
        {
            LogCannotOpenOnce(device);
            return false;
        }
        _devicesFailedToOpen.Remove(device.Key);
        _channels.Add(channel);
        if (channel.HasShortCollection)
            EnableReceiverNotifications(channel, cancellationToken);
        return true;
    }

    // A device that cannot be opened (during a resume or enumeration, or held exclusively by another program) is tried again
    // at the next read, not only at the next USB change: Refresh reports it and the provider rediscovers. Each device is logged
    // once until it opens, so a blocked one does not fill the log.
    // Cost: while a device stays blocked, every read discovers again (a few HID requests per minute).
    private void LogCannotOpenOnce(HidPpDevice device)
    {
        if (_devicesFailedToOpen.Add(device.Key))
            AppLog.Warn("Cannot open", ("receiver", HidPpDevices.LogId(device.Key)));
    }

    /// <summary>
    /// Switches on the receiver's "wireless" notifications (mouse connected / disconnected). The receiver resets this
    /// flag on every replug, so it is checked on each open; we only write when the flag is off and change only that bit.
    /// </summary>
    private static void EnableReceiverNotifications(HidPpChannel channel, CancellationToken cancellationToken)
    {
        try
        {
            var read = channel.Exchange(HidPpMessage.BuildGetRegister(HidPpMessage.ReceiverIndex, ReceiverNotifications.FlagsRegister), DeviceDiscovery.Timeout, cancellationToken);
            if (read.Response is not { } response)
                return; // not a receiver (e.g. the cable answers with an error)

            if (!ReceiverNotifications.TryEnableWireless(HidPpMessage.ParamsOf(response), out var updatedFlags))
                return;

            var write = HidPpMessage.BuildSetRegister(HidPpMessage.ReceiverIndex, ReceiverNotifications.FlagsRegister, updatedFlags);
            var written = channel.Exchange(write, DeviceDiscovery.Timeout, cancellationToken);
            if (written.Response is null)
                AppLog.Warn("Receiver notifications failed");
            else
                AppLog.Info("Receiver notifications enabled");
        }
        catch (Exception ex) when (ex is IOException or TimeoutException)
        {
            AppLog.Warn("Receiver notifications failed", AppLog.Failure(ex));
        }
    }
}
