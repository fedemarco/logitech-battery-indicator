using HidSharp;
using LogitechBattery.Battery;
using LogitechBattery.HidPp;

namespace LogitechBattery;

/// <summary>
/// Reads the mouse battery over HID++ 2.0 (Unified Battery), via Lightspeed receiver or USB cable.
/// Keeps every HID++ device open so wake-ups, sleeps and battery events are noticed at once.
/// </summary>
public sealed class HidPpBatteryProvider : IBatteryProvider
{
    private static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(1);

    // Shown only while no device was found yet this session, so the app shows what it showed before multi-device support.
    private static readonly DeviceInfo PlaceholderDevice = new(DeviceId.FromRoute(0, 0), "PRO X2 SUPERSTRIKE");

    /// <summary>The outcome of one read attempt; <see cref="ChannelLost"/> asks for one immediate second attempt.</summary>
    private sealed record ReadPass(IReadOnlyList<DeviceReading> Readings, bool ChannelLost);

    private readonly object _readLock = new();
    private readonly List<HidPpChannel> _channels = [];
    private readonly DeviceRoutes _routes = new();
    private readonly Dictionary<DeviceId, ConnectionState> _lastStates = [];
    private volatile bool _rediscover = true;
    private volatile bool _reopen;
    private readonly HashSet<string> _devicesFailedToOpen = [];

    /// <summary>Raised (on a background thread) when the reading may have changed: USB devices added or removed, a device connecting to or disconnecting from the receiver, or a battery event.</summary>
    public event Action? ChangeDetected;

    public HidPpBatteryProvider() => DeviceList.Local.Changed += OnUsbDevicesChanged;

    public Task<IReadOnlyList<DeviceReading>> ReadAsync(CancellationToken cancellationToken = default) => Task.Run(() => Read(cancellationToken), cancellationToken);

    /// <summary>
    /// Closes and opens every device again at the next read. Call after the PC resumed: the receiver may have forgotten
    /// its notification flag (as after a replug) while the handles still look valid, and nothing else would notice.
    /// </summary>
    public void ReopenDevices() => _reopen = true;

    private void OnUsbDevicesChanged(object? sender, DeviceListChangedEventArgs e)
    {
        AppLog.Write("USB devices changed");
        _rediscover = true; // the best route may have changed (cable preferred over receiver)
        ChangeDetected?.Invoke();
    }

    private void OnReport(HidPpChannel channel, byte[] report)
    {
        if (ReceiverNotifications.ReadConnection(report) is { } connection)
        {
            AppLog.Write(connection.LinkEstablished ? "Device link up" : "Device link down");
            ChangeDetected?.Invoke();
        }
        else if (_routes.IsBatteryEventSource(channel, report))
        {
            AppLog.Write("Battery event");
            ChangeDetected?.Invoke();
        }
    }

    private IReadOnlyList<DeviceReading> Read(CancellationToken cancellationToken)
    {
        lock (_readLock)
        {
            try
            {
                // A lost channel is followed by one immediate rediscovery (e.g. cable unplugged while the receiver is still there).
                var pass = ReadCore(cancellationToken);
                if (pass.ChannelLost)
                    pass = ReadCore(cancellationToken);
                return pass.Readings;
            }
            catch (Exception ex)
            {
                AppLog.Write($"Battery read failed: {ex}");
                throw;
            }
        }
    }

    private ReadPass ReadCore(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.Now;
        if (_reopen)
        {
            _reopen = false;
            CloseAllChannels();
            _rediscover = true;
        }
        if (_rediscover || _channels.Any(channel => channel.IsClosed))
        {
            _routes.Clear();
            _rediscover = false; // cleared first, so a USB event during the refresh is not lost
            try
            {
                RefreshChannels(cancellationToken);
            }
            catch
            {
                _rediscover = true; // a failed refresh is retried
                throw;
            }
        }

        _routes.DropRoutesOnClosedChannels();
        var channelLost = LoseChannels(_routes.Discover(_channels, cancellationToken));

        var statuses = new Dictionary<DeviceId, BatteryStatus?>();
        foreach (var route in _routes.All)
            channelLost |= ReadStatus(route, now, statuses, cancellationToken);
        return new ReadPass(ToReadings(statuses, now), channelLost);
    }

    /// <summary>Reads one routed device into <paramref name="statuses"/>; true if its channel was lost.</summary>
    private bool ReadStatus(BatteryTarget route, DateTimeOffset now, Dictionary<DeviceId, BatteryStatus?> statuses, CancellationToken cancellationToken)
    {
        try
        {
            var request = HidPpMessage.BuildRequest(route.DeviceIndex, route.FeatureIndex, UnifiedBattery.GetStatusFunction, HidPpMessage.SoftwareId);
            var answer = route.Channel.Exchange(request, ResponseTimeout, cancellationToken);

            if (answer.ErrorCode is { } errorCode)
            {
                // The device answered with an error: not asleep. Indexes may be stale (e.g. re-paired): rediscover next time.
                AppLog.Write($"getStatus failed with HID++ 2.0 error 0x{errorCode:X2} ({route.Device.Name})");
                statuses[route.Device.Id] = null;
                _routes.Drop(route);
            }
            else if (answer.Response is not { } response)
            {
                // Rediscover next time (the device may be paired to another slot); the channel stays open to hear it wake up.
                _routes.Drop(route);
            }
            else
            {
                statuses[route.Device.Id] = UnifiedBattery.ParseStatus(HidPpMessage.ParamsOf(response), now);
            }
            return false;
        }
        catch (FormatException ex)
        {
            AppLog.Write($"getStatus answer of {route.Device.Name} is malformed: {ex.Message}");
            statuses[route.Device.Id] = null;
            _routes.Drop(route);
            return false;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException)
        {
            Lost(route.Channel); // unplugged mid-read
            return true;
        }
    }

    /// <summary>One reading per known device, in first-seen order; a device that did not answer is Asleep or NotFound.</summary>
    private IReadOnlyList<DeviceReading> ToReadings(Dictionary<DeviceId, BatteryStatus?> statuses, DateTimeOffset now)
    {
        if (_routes.Known.IsEmpty)
            return [new DeviceReading(PlaceholderDevice, NoReading(PlaceholderDevice, MissingDeviceState(), now))];

        var openChannelKeys = _channels.Where(channel => !channel.IsClosed).Select(channel => channel.Key).ToHashSet();
        return _routes.Known.InFirstSeenOrder.Select(device =>
        {
            if (statuses.TryGetValue(device.Id, out var status))
            {
                if (status is not null)
                    LogState(device, status.Connection);
                return new DeviceReading(device, status);
            }
            var state = _routes.Known.StateWhenNotAnswering(device, openChannelKeys);
            return new DeviceReading(device, NoReading(device, state, now));
        }).ToList();
    }

    private bool LoseChannels(IReadOnlyList<HidPpChannel> channels)
    {
        foreach (var channel in channels)
            Lost(channel);
        return channels.Count > 0;
    }

    private void Lost(HidPpChannel channel)
    {
        AppLog.Write($"Device lost: {channel.Key}");
        channel.Dispose();
        _channels.Remove(channel);
        _routes.DropRoutesOn(channel);
        _rediscover = true;
    }

    private void CloseAllChannels()
    {
        foreach (var channel in _channels)
            channel.Dispose();
        _channels.Clear();
        _devicesFailedToOpen.Clear(); // after a resume, a device that still cannot be opened is logged again
        _routes.Clear();
    }

    // A receiver without an answering mouse means the mouse sleeps or is off.
    // shortcut: any open HID++ device counts as that receiver, so another Logitech device alone reads as Asleep; the placeholder disappears once a device with Unified Battery is found.
    private ConnectionState MissingDeviceState() =>
        _channels.Any(channel => !channel.IsClosed) ? ConnectionState.Asleep : ConnectionState.NotFound;

    private BatteryStatus NoReading(DeviceInfo device, ConnectionState state, DateTimeOffset now)
    {
        LogState(device, state);
        return new BatteryStatus(state, null, ChargingState.Unknown, now);
    }

    private void LogState(DeviceInfo device, ConnectionState state)
    {
        if (_lastStates.TryGetValue(device.Id, out var lastState) && lastState == state)
            return;
        _lastStates[device.Id] = state;
        AppLog.Write($"Connection: {device.Name} {state}");
    }

    private void RefreshChannels(CancellationToken cancellationToken)
    {
        foreach (var closed in _channels.Where(channel => channel.IsClosed).ToList())
        {
            closed.Dispose();
            _channels.Remove(closed);
        }

        var devices = HidPpDevices.Find();
        _devicesFailedToOpen.IntersectWith(devices.Select(device => device.Key)); // an unplugged device is logged again if it fails after a replug

        foreach (var device in devices.Where(device => _channels.All(channel => channel.Key != device.Key)))
        {
            if (HidPpChannel.Open(device, OnReport) is not { } channel)
            {
                RetryOpenOnNextRead(device);
                continue;
            }
            _devicesFailedToOpen.Remove(device.Key);
            _channels.Add(channel);
            if (channel.HasShortCollection)
                EnableReceiverNotifications(channel, cancellationToken);
        }
    }

    // A device that cannot be opened (during a resume or enumeration, or held exclusively by another program) is tried again
    // at the next read, not only at the next USB change. Each device is logged once until it opens, so a blocked one does not fill the log.
    // Cost: while a device stays blocked, every read discovers again (a few HID requests per minute).
    private void RetryOpenOnNextRead(HidPpDevice device)
    {
        _rediscover = true;
        if (_devicesFailedToOpen.Add(device.Key))
            AppLog.Write($"Cannot open {device.Key}");
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
            AppLog.Write(written.Response is null ? "Receiver: enabling wireless notifications failed" : "Receiver: wireless notifications enabled");
        }
        catch (Exception ex) when (ex is IOException or TimeoutException)
        {
            AppLog.Write($"Receiver: enabling wireless notifications failed: {ex.Message}");
        }
    }
}
