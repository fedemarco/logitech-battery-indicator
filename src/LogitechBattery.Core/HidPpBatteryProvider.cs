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

    internal enum RouteOutcome { Answered, NoAnswer, Failed, ChannelLost }

    /// <summary>What reading one route gave; <see cref="Status"/> is set only for <see cref="RouteOutcome.Answered"/>.</summary>
    internal readonly record struct RouteRead(RouteOutcome Outcome, BatteryStatus? Status = null);

    /// <summary>The outcome of one read attempt; <see cref="ChannelLost"/> asks for one immediate second attempt.</summary>
    private sealed record ReadPass(IReadOnlyList<DeviceReading> Readings, bool ChannelLost);

    private readonly object _readLock = new();
    private readonly List<HidPpChannel> _channels = [];
    private readonly DeviceRoutes _routes = new();
    private readonly Dictionary<DeviceId, ConnectionState> _lastStates = [];
    private volatile bool _rediscover = true;
    private volatile bool _reopen;
    private volatile bool _forgetSkippedSlots;
    private readonly HashSet<string> _devicesFailedToOpen = [];

    /// <summary>Raised (on a background thread) when the reading may have changed: USB devices added or removed, a device connecting to or disconnecting from the receiver, or a battery event.</summary>
    public event Action? ChangeDetected;

    public HidPpBatteryProvider() => DeviceList.Local.Changed += OnUsbDevicesChanged;

    public Task<IReadOnlyList<DeviceReading>> ReadAsync(CancellationToken cancellationToken = default) => Task.Run(() => Read(cancellationToken), cancellationToken);

    /// <summary>
    /// Closes and opens every device again at the next read. Call after the PC resumed: the receiver may have forgotten
    /// its notification flag (as after a replug) while the handles still look valid, and nothing else would notice.
    /// </summary>
    public void ReopenDevices()
    {
        _reopen = true;
        _forgetSkippedSlots = true;
    }

    private void OnUsbDevicesChanged(object? sender, DeviceListChangedEventArgs e)
    {
        AppLog.Info("USB devices changed");
        _rediscover = true; // the best route may have changed (cable preferred over receiver)
        _forgetSkippedSlots = true; // a device may have been paired into a skipped slot
        ChangeDetected?.Invoke();
    }

    private void OnReport(HidPpChannel channel, byte[] report)
    {
        if (ReceiverNotifications.ReadConnection(report) is { } connection)
        {
            AppLog.Info(connection.LinkEstablished ? "Link up" : "Link down", LinkFields(channel, connection.DeviceIndex));
            ChangeDetected?.Invoke();
        }
        else if (_routes.IsBatteryEventSource(channel, report))
        {
            AppLog.Info("Battery event");
            ChangeDetected?.Invoke();
        }
    }

    // Runs on a reader thread: the device name comes only from the routes (immutable array), never from KnownDevices.
    private (string Key, object? Value)[] LinkFields(HidPpChannel channel, byte deviceIndex)
    {
        var route = _routes.All.FirstOrDefault(candidate => candidate.Channel == channel && candidate.DeviceIndex == deviceIndex);
        (string Key, object? Value) devIdx = ("devIdx", $"{deviceIndex:X2}"), receiver = ("receiver", channel.Key);
        return route is null ? [receiver, devIdx] : [receiver, devIdx, ("device", route.Device.Name)];
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
                AppLog.Error("Battery read failed", AppLog.FailureWithStack(ex));
                throw;
            }
        }
    }

    private ReadPass ReadCore(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.Now;
        ApplyRequestedResets();
        if (_rediscover || _channels.Any(channel => channel.IsClosed))
            RefreshChannelsAndForgetRoutes(cancellationToken);

        _routes.DropRoutesOnClosedChannels();
        var channelLost = LoseChannels(_routes.Discover(_channels, cancellationToken));
        var statuses = new Dictionary<DeviceId, BatteryStatus?>();
        channelLost |= ReadRoutedDevices(now, statuses, cancellationToken);
        return new ReadPass(ToReadings(statuses, now), channelLost);
    }

    private void ApplyRequestedResets()
    {
        if (_reopen)
        {
            _reopen = false;
            CloseAllChannels();
            _rediscover = true;
        }
        if (_forgetSkippedSlots)
        {
            _forgetSkippedSlots = false;
            _routes.ForgetSkippedSlots();
        }
    }

    private void RefreshChannelsAndForgetRoutes(CancellationToken cancellationToken)
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

    /// <summary>Reads every routed device into <paramref name="statuses"/>; true if a channel was lost.</summary>
    private bool ReadRoutedDevices(DateTimeOffset now, Dictionary<DeviceId, BatteryStatus?> statuses, CancellationToken cancellationToken)
    {
        var channelLost = false;
        foreach (var route in _routes.All)
        {
            var read = ReadStatus(route, now, cancellationToken);
            switch (read.Outcome)
            {
                case RouteOutcome.Answered:
                    statuses[route.Device.Id] = read.Status;
                    break;
                case RouteOutcome.NoAnswer:
                    _routes.Drop(route); // rediscover next time (the device may be paired to another slot); the channel stays open to hear it wake up
                    break;
                case RouteOutcome.Failed:
                    statuses[route.Device.Id] = null;
                    _routes.Drop(route); // indexes may be stale (e.g. re-paired): rediscover next time
                    break;
                case RouteOutcome.ChannelLost:
                    Lost(route.Channel);
                    channelLost = true;
                    break;
            }
        }
        return channelLost;
    }

    internal static RouteRead ReadStatus(BatteryTarget route, DateTimeOffset now, CancellationToken cancellationToken)
    {
        try
        {
            var request = HidPpMessage.BuildRequest(route.DeviceIndex, route.FeatureIndex, UnifiedBattery.GetStatusFunction, HidPpMessage.SoftwareId);
            var answer = route.Channel.Exchange(request, ResponseTimeout, cancellationToken);
            return ReadAnswer(route, answer, now);
        }
        catch (Exception ex) when (ex is IOException or TimeoutException)
        {
            return new RouteRead(RouteOutcome.ChannelLost);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // one faulty device must not fail the reading of the others
            // shortcut: logged at every poll while the device keeps failing (bounded by log rotation); log once per device if a real device ever does this
            AppLog.Warn("getStatus failed", [("device", route.Device.Name), .. AppLog.Failure(ex)]);
            return new RouteRead(RouteOutcome.Failed);
        }
    }

    internal static RouteRead ReadAnswer(BatteryTarget route, HidPpAnswer answer, DateTimeOffset now)
    {
        if (answer.ErrorCode is { } errorCode)
        {
            // The device answered with an error: not asleep.
            AppLog.Warn("getStatus returned an error", ("device", route.Device.Name), ("errorCode", $"0x{errorCode:X2}"));
            return new RouteRead(RouteOutcome.Failed);
        }
        if (answer.Response is not { } response)
            return new RouteRead(RouteOutcome.NoAnswer);

        try
        {
            return new RouteRead(RouteOutcome.Answered, UnifiedBattery.ParseStatus(HidPpMessage.ParamsOf(response), now));
        }
        catch (FormatException ex)
        {
            AppLog.Warn("getStatus answer is malformed", [("device", route.Device.Name), .. AppLog.Failure(ex)]);
            return new RouteRead(RouteOutcome.Failed);
        }
    }

    /// <summary>One reading per known device, in first-seen order; a device that did not answer is Asleep or NotFound.</summary>
    private IReadOnlyList<DeviceReading> ToReadings(Dictionary<DeviceId, BatteryStatus?> statuses, DateTimeOffset now)
    {
        if (_routes.Known.IsEmpty)
            return [new DeviceReading(DeviceInfo.NoDeviceYet, NoReading(DeviceInfo.NoDeviceYet, MissingDeviceState(), now))];

        var openChannelKeys = _channels.Where(channel => !channel.IsClosed).Select(channel => channel.Key).ToHashSet();
        return _routes.Known.InFirstSeenOrder.Select(device => ReadingOf(device, statuses, openChannelKeys, now)).ToList();
    }

    private DeviceReading ReadingOf(DeviceInfo device, Dictionary<DeviceId, BatteryStatus?> statuses, IReadOnlySet<string> openChannelKeys, DateTimeOffset now)
    {
        if (!statuses.TryGetValue(device.Id, out var status))
        {
            var state = _routes.Known.StateWhenNotAnswering(device, openChannelKeys);
            return new DeviceReading(device, NoReading(device, state, now));
        }
        if (status is not null)
            LogState(device, status.Connection);
        return new DeviceReading(device, status);
    }

    private bool LoseChannels(IReadOnlyList<IHidPpChannel> channels)
    {
        foreach (var channel in channels)
            Lost(channel);
        return channels.Count > 0;
    }

    private void Lost(IHidPpChannel lostChannel)
    {
        AppLog.Info("Device lost", ("receiver", lostChannel.Key));
        if (lostChannel is HidPpChannel channel) // discovery and routes know only the interface; every channel they get is one of ours
        {
            channel.Dispose();
            _channels.Remove(channel);
        }
        _routes.DropRoutesOn(lostChannel);
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
        AppLog.Info("Connection", ("device", device.Name), ("state", state));
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
            AppLog.Warn("Cannot open", ("receiver", device.Key));
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
