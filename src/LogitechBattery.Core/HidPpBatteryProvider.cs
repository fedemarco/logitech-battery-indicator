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

    // Shown while no device was found yet this session; step 4 of multi-device support replaces it.
    private static readonly DeviceInfo UnknownDevice = new(DeviceId.FromRoute(0, 0), "PRO X2 SUPERSTRIKE");

    private readonly object _readLock = new();
    private readonly List<HidPpChannel> _channels = [];
    private volatile BatteryTarget? _target;
    private DeviceInfo? _lastDevice;
    private ConnectionState? _lastState;
    private volatile bool _rediscover = true;
    private volatile bool _reopen;
    private readonly HashSet<string> _devicesFailedToOpen = [];

    /// <summary>Raised (on a background thread) when the reading may have changed: USB devices added or removed, the mouse connecting to or disconnecting from the receiver, or a battery event.</summary>
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
        var target = _target;
        if (ReceiverNotifications.ReadConnection(report) is { } connection)
        {
            if (target is null || (target.Channel == channel && target.DeviceIndex == connection.DeviceIndex))
            {
                AppLog.Write(connection.LinkEstablished ? "Mouse link up" : "Mouse link down");
                ChangeDetected?.Invoke();
            }
        }
        else if (target is not null && target.Channel == channel && HidPpMessage.IsEventFrom(report, target.DeviceIndex, target.FeatureIndex))
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
                // A lost device is followed by one immediate rediscovery (e.g. cable unplugged while the receiver is still there).
                var status = ReadCore(cancellationToken) ?? ReadCore(cancellationToken) ?? NoReading(ConnectionState.NotFound, DateTimeOffset.Now);
                return [new DeviceReading(_lastDevice ?? UnknownDevice, status)];
            }
            catch (Exception ex)
            {
                AppLog.Write($"Battery read failed: {ex}");
                throw;
            }
        }
    }

    /// <summary>One read attempt; null if the device was lost during it.</summary>
    private BatteryStatus? ReadCore(CancellationToken cancellationToken)
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
            ForgetTarget();
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

        _target ??= DeviceDiscovery.Discover(_channels, cancellationToken);
        if (_target is not { } target)
            return NoReading(MissingTargetState(), now);

        _lastDevice = target.Device;
        return ReadStatus(target, now, cancellationToken);
    }

    private BatteryStatus? ReadStatus(BatteryTarget target, DateTimeOffset now, CancellationToken cancellationToken)
    {
        try
        {
            var request = HidPpMessage.BuildRequest(target.DeviceIndex, target.FeatureIndex, UnifiedBattery.GetStatusFunction, HidPpMessage.SoftwareId);
            var answer = target.Channel.Exchange(request, ResponseTimeout, cancellationToken);

            if (answer.ErrorCode is { } errorCode)
            {
                // The device answered with an error: not asleep.
                AppLog.Write($"getStatus failed with HID++ 2.0 error 0x{errorCode:X2}");
                ForgetTarget(); // indexes may be stale (e.g. re-paired): rediscover next time
                throw new InvalidOperationException($"Battery status request failed (HID++ error 0x{errorCode:X2}).");
            }
            if (answer.Response is not { } response)
            {
                ForgetTarget(); // rediscover next time (the mouse may be paired to another slot); the channel stays open to hear it wake up
                return NoReading(ConnectionState.Asleep, now);
            }

            var status = UnifiedBattery.ParseStatus(HidPpMessage.ParamsOf(response), now);
            LogState(ConnectionState.Connected);
            return status;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException)
        {
            Lost(target); // unplugged mid-read
            return null;
        }
    }

    private void Lost(BatteryTarget target)
    {
        AppLog.Write($"Device lost: {target.Channel.Key}");
        target.Channel.Dispose();
        _channels.Remove(target.Channel);
        ForgetTarget();
        _rediscover = true;
    }

    private void CloseAllChannels()
    {
        foreach (var channel in _channels)
            channel.Dispose();
        _channels.Clear();
        _devicesFailedToOpen.Clear(); // after a resume, a device that still cannot be opened is logged again
        ForgetTarget();
    }

    private void ForgetTarget() => _target = null;

    // A receiver without an answering mouse means the mouse sleeps or is off.
    // shortcut: any open HID++ device counts as that receiver, so another Logitech device alone reads as Asleep; revisit with multi-device support.
    private ConnectionState MissingTargetState() =>
        _channels.Any(channel => !channel.IsClosed) ? ConnectionState.Asleep : ConnectionState.NotFound;

    private BatteryStatus NoReading(ConnectionState state, DateTimeOffset now)
    {
        LogState(state);
        return new BatteryStatus(state, null, ChargingState.Unknown, now);
    }

    private void LogState(ConnectionState state)
    {
        if (state == _lastState)
            return;
        _lastState = state;
        AppLog.Write($"Connection: {state}");
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
