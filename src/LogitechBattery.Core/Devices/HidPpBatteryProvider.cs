using HidSharp;
using LogitechBattery.Battery;
using LogitechBattery.HidPp;
using LogitechBattery.Logging;

namespace LogitechBattery.Devices;

/// <summary>
/// Reads the mouse battery over HID++ 2.0 (Unified Battery), via Lightspeed receiver or USB cable.
/// Keeps every HID++ device open so wake-ups, sleeps and battery events are noticed at once.
/// </summary>
public sealed class HidPpBatteryProvider : IBatteryProvider
{
    /// <summary>The outcome of one read attempt; <see cref="ChannelLost"/> asks for one immediate second attempt.</summary>
    private sealed record ReadPass(IReadOnlyList<DeviceReading> Readings, bool ChannelLost);

    private readonly object _readLock = new();
    private readonly HidPpChannels _channels;
    private readonly DeviceRoutes _routes = new();
    private readonly AnswerTolerance _answerTolerance = new();
    private readonly ReportedLinkDowns _linkDowns = new();
    private readonly DeviceReadingsBuilder _readingsBuilder = new();
    private volatile bool _rediscover = true;
    private volatile bool _reopen;
    private volatile bool _forgetSkippedSlots;

    /// <summary>Raised (on a background thread) when the reading may have changed: USB devices added or removed, a device connecting to or disconnecting from the receiver, or a battery event.</summary>
    public event Action? ChangeDetected;

    public HidPpBatteryProvider()
    {
        _channels = new HidPpChannels(OnReport);
        DeviceList.Local.Changed += OnUsbDevicesChanged;
    }

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
        _rediscover = true; // a channel may have come or gone: open new ones, drop the routes on closed ones
        _forgetSkippedSlots = true; // a device may have been paired into a skipped slot
        ChangeDetected?.Invoke();
    }

    private void OnReport(HidPpChannel channel, byte[] report)
    {
        if (ReceiverNotifications.ReadConnection(report) is { } connection)
        {
            AppLog.Info(connection.LinkEstablished ? "Link up" : "Link down", LinkFields(channel, connection.DeviceIndex));
            RecordLink(new Slot(channel.Key, connection.DeviceIndex), connection.LinkEstablished);
            ChangeDetected?.Invoke();
        }
        else if (_routes.IsBatteryEventSource(channel, report))
        {
            AppLog.Info("Battery event");
            ChangeDetected?.Invoke();
        }
    }

    private void RecordLink(Slot slot, bool linkEstablished)
    {
        if (linkEstablished)
            _linkDowns.Forget(slot);
        else
            _linkDowns.Record(slot);
    }

    // Runs on a reader thread: the device name comes only from the routes' name map (immutable), never from KnownDevices.
    private (string Key, object? Value)[] LinkFields(HidPpChannel channel, byte deviceIndex)
    {
        var name = _routes.NameAt(new Slot(channel.Key, deviceIndex));
        (string Key, object? Value) devIdx = ("devIdx", $"{deviceIndex:X2}"), receiver = ("receiver", HidPpDevices.LogId(channel.Key));
        return name is null ? [receiver, devIdx] : [receiver, devIdx, ("device", name)];
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
        if (_rediscover || _channels.AnyClosed)
            RefreshChannelsForRediscovery(cancellationToken);

        _routes.DropRoutesOnClosedChannels();
        var channelLost = LoseChannels(_routes.Discover(_channels.All, cancellationToken));
        var statuses = new Dictionary<DeviceId, BatteryStatus?>();
        channelLost |= ReadRoutedDevices(now, statuses, cancellationToken);
        var readings = _readingsBuilder.Build(_routes.Known, statuses, _channels.OpenKeys(), now);
        return new ReadPass(readings, channelLost);
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

    private void RefreshChannelsForRediscovery(CancellationToken cancellationToken)
    {
        _rediscover = false; // cleared first, so a USB event during the refresh is not lost
        try
        {
            if (_channels.Refresh(cancellationToken))
                _rediscover = true; // a device that could not be opened is tried again at the next read
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
            var lostThisRoute = route.Channel.IsClosed
                ? LoseClosedChannel(route)
                : ReadRoute(route, now, statuses, cancellationToken);
            if (lostThisRoute)
                channelLost = true;
        }
        return channelLost;
    }

    // A closed channel (unplugged, or lost for an earlier device of the same receiver in this pass) is lost once;
    // reading its other devices would only log the loss again. The second read pass reads them anew.
    private bool LoseClosedChannel(BatteryTarget route)
    {
        if (_channels.Contains(route.Channel))
            Lost(route.Channel);
        return true;
    }

    /// <summary>Reads one route and keeps its outcome; true if its channel was lost.</summary>
    private bool ReadRoute(BatteryTarget route, DateTimeOffset now, Dictionary<DeviceId, BatteryStatus?> statuses, CancellationToken cancellationToken)
    {
        var read = BatteryStatusRequest.ReadStatus(route, now, cancellationToken);
        // Taken after the read, for every outcome: a link down that arrived during the read must count, and an answer clears it.
        var linkWentDown = _linkDowns.Take(route.Slot);
        switch (read)
        {
            case { Outcome: BatteryStatusRequest.RouteOutcome.Answered, Status: { } status }:
                KeepAnswer(route, status, statuses);
                return false;
            case { Outcome: BatteryStatusRequest.RouteOutcome.NoAnswer }:
                KeepOrDropMissedRoute(route, linkWentDown, statuses);
                return false;
            case { Outcome: BatteryStatusRequest.RouteOutcome.Failed }:
                ReportFailure(route, statuses);
                return false;
            case { Outcome: BatteryStatusRequest.RouteOutcome.ChannelLost }:
                Lost(route.Channel);
                return true;
            default:
                return false;
        }
    }

    private void KeepAnswer(BatteryTarget route, BatteryStatus status, Dictionary<DeviceId, BatteryStatus?> statuses)
    {
        statuses[route.Device.Id] = status;
        _answerTolerance.Answered(route.Device.Id, status, Environment.TickCount64);
    }

    private void ReportFailure(BatteryTarget route, Dictionary<DeviceId, BatteryStatus?> statuses)
    {
        statuses[route.Device.Id] = null;
        _answerTolerance.Failed(route.Device.Id);
        _routes.Drop(route); // indexes may be stale (e.g. re-paired): rediscover next time
    }

    private void KeepOrDropMissedRoute(BatteryTarget route, bool linkWentDown, Dictionary<DeviceId, BatteryStatus?> statuses)
    {
        if (_answerTolerance.KeepOrForgetAfterMissedRead(route.Device.Id, linkWentDown, Environment.TickCount64) is { } keptStatus)
        {
            statuses[route.Device.Id] = keptStatus;
            return;
        }
        _routes.Drop(route); // rediscover next time (the device may be paired to another slot); the channel stays open to hear it wake up
    }

    private bool LoseChannels(IReadOnlyList<IHidPpChannel> channels)
    {
        foreach (var channel in channels)
            Lost(channel);
        return channels.Count > 0;
    }

    private void Lost(IHidPpChannel lostChannel)
    {
        AppLog.Info("Device lost", ("receiver", HidPpDevices.LogId(lostChannel.Key)));
        // Discovery and routes know only the interface; every channel they get is one of ours.
        if (lostChannel is HidPpChannel channel)
            _channels.Remove(channel);
        ForgetDevicesOn(lostChannel);
        _routes.DropRoutesOn(lostChannel);
        _rediscover = true;
    }

    // A status from before the channel was lost must never be kept as current.
    private void ForgetDevicesOn(IHidPpChannel channel)
    {
        foreach (var route in _routes.All.Where(route => route.Channel == channel))
        {
            _answerTolerance.Forget(route.Device.Id);
            _linkDowns.Forget(route.Slot);
        }
    }

    private void CloseAllChannels()
    {
        _answerTolerance.Clear();
        _linkDowns.Clear();
        _channels.CloseAll();
        _routes.Clear();
    }
}
