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
    private static readonly TimeSpan DiscoveryTimeout = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(1);

    // shortcut: single supported model until device selection (multiple devices) is implemented
    private const string SupportedDeviceName = "PRO X2 SUPERSTRIKE";

    private sealed record Target(HidPpChannel Channel, byte DeviceIndex, byte FeatureIndex);

    private readonly object _readLock = new();
    private readonly List<HidPpChannel> _channels = [];
    private volatile Target? _target;
    private ConnectionState? _lastState;
    private volatile bool _rediscover = true;

    /// <summary>Raised (on a background thread) when the reading may have changed: USB devices added or removed, the mouse connecting to or disconnecting from the receiver, or a battery event.</summary>
    public event Action? ChangeDetected;

    public HidPpBatteryProvider() => DeviceList.Local.Changed += OnUsbDevicesChanged;

    public Task<BatteryStatus> ReadAsync(CancellationToken cancellationToken = default) => Task.Run(() => Read(cancellationToken), cancellationToken);

    private void OnUsbDevicesChanged(object? sender, DeviceListChangedEventArgs e)
    {
        AppLog.Write("USB devices changed");
        _rediscover = true; // the best route may have changed (cable preferred over receiver)
        ChangeDetected?.Invoke();
    }

    private void OnReport(HidPpChannel channel, byte[] report)
    {
        var target = _target;
        if (ReceiverNotifications.TryReadConnection(report, out var deviceIndex, out var linkEstablished))
        {
            if (target is null || (target.Channel == channel && target.DeviceIndex == deviceIndex))
            {
                AppLog.Write(linkEstablished ? "Mouse link up" : "Mouse link down");
                ChangeDetected?.Invoke();
            }
        }
        else if (target is not null && target.Channel == channel && HidPpMessage.IsEventFrom(report, target.DeviceIndex, target.FeatureIndex))
        {
            AppLog.Write("Battery event");
            ChangeDetected?.Invoke();
        }
    }

    private BatteryStatus Read(CancellationToken ct)
    {
        lock (_readLock)
        {
            try
            {
                // A lost device is followed by one immediate rediscovery (e.g. cable unplugged while the receiver is still there).
                return ReadCore(ct) ?? ReadCore(ct) ?? NoReading(ConnectionState.NotFound, DateTimeOffset.Now);
            }
            catch (Exception ex)
            {
                AppLog.Write($"Battery read failed: {ex}");
                throw;
            }
        }
    }

    /// <summary>One read attempt; null if the device was lost during it.</summary>
    private BatteryStatus? ReadCore(CancellationToken ct)
    {
        var now = DateTimeOffset.Now;
        if (_rediscover || _channels.Any(c => c.IsClosed))
        {
            ForgetTarget();
            _rediscover = false; // cleared first, so a USB event during the refresh is not lost
            try
            {
                RefreshChannels(ct);
            }
            catch
            {
                _rediscover = true; // a failed refresh is retried
                throw;
            }
        }

        _target ??= Discover(ct);
        if (_target is not { } target)
            return NoReading(MissingTargetState(), now);

        return ReadStatus(target, now, ct);
    }

    private BatteryStatus? ReadStatus(Target target, DateTimeOffset now, CancellationToken ct)
    {
        try
        {
            var request = HidPpMessage.BuildRequest(target.DeviceIndex, target.FeatureIndex, UnifiedBattery.GetStatusFunction, HidPpMessage.SoftwareId);
            var answer = target.Channel.Exchange(request, ResponseTimeout, ct);

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

    private void Lost(Target target)
    {
        AppLog.Write($"Device lost: {target.Channel.Key}");
        target.Channel.Dispose();
        _channels.Remove(target.Channel);
        ForgetTarget();
        _rediscover = true;
    }

    private void ForgetTarget() => _target = null;

    // A receiver without an answering mouse means the mouse sleeps or is off.
    private ConnectionState MissingTargetState() =>
        _channels.Any(c => !c.IsClosed) ? ConnectionState.Asleep : ConnectionState.NotFound;

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

    private void RefreshChannels(CancellationToken ct)
    {
        foreach (var closed in _channels.Where(c => c.IsClosed).ToList())
        {
            closed.Dispose();
            _channels.Remove(closed);
        }

        foreach (var device in HidPpDevices.Find().Where(d => _channels.All(c => c.Key != d.Key)))
        {
            if (HidPpChannel.Open(device, OnReport) is not { } channel)
                continue;
            _channels.Add(channel);
            if (channel.HasShortCollection)
                EnableReceiverNotifications(channel, ct);
        }
    }

    /// <summary>
    /// Switches on the receiver's "wireless" notifications (mouse connected / disconnected). The receiver resets this
    /// flag on every replug, so it is checked on each open; we only write when the flag is off and change only that bit.
    /// </summary>
    private static void EnableReceiverNotifications(HidPpChannel channel, CancellationToken ct)
    {
        try
        {
            var read = channel.Exchange(HidPpMessage.BuildGetRegister(HidPpMessage.ReceiverIndex, ReceiverNotifications.FlagsRegister), DiscoveryTimeout, ct);
            if (read.Response is not { } response)
                return; // not a receiver (e.g. the cable answers with an error)

            var flags = HidPpMessage.ParamsOf(response);
            if (ReceiverNotifications.HasWireless(flags))
                return;

            var write = HidPpMessage.BuildSetRegister(HidPpMessage.ReceiverIndex, ReceiverNotifications.FlagsRegister, ReceiverNotifications.WithWireless(flags));
            var written = channel.Exchange(write, DiscoveryTimeout, ct);
            AppLog.Write(written.Response is null ? "Receiver: enabling wireless notifications failed" : "Receiver: wireless notifications enabled");
        }
        catch (Exception ex) when (ex is IOException or TimeoutException)
        {
            AppLog.Write($"Receiver: enabling wireless notifications failed: {ex.Message}");
        }
    }

    /// <summary>Pass 1: the cable's device index; pass 2: the receiver slots. First HID++ 2.0 device with Unified Battery wins.</summary>
    private Target? Discover(CancellationToken ct)
    {
        foreach (var deviceIndexes in new[] { HidPpMessage.CableDeviceIndexes, HidPpMessage.ReceiverDeviceIndexes })
        {
            foreach (var channel in _channels.Where(c => !c.IsClosed))
            {
                if (FindTarget(channel, deviceIndexes, ct) is { } target)
                    return target;
            }
        }
        return null;
    }

    private static Target? FindTarget(HidPpChannel channel, byte[] deviceIndexes, CancellationToken ct)
    {
        foreach (var deviceIndex in deviceIndexes)
        {
            try
            {
                if (FindBatteryFeatureIndex(channel, deviceIndex, ct) is not { } featureIndex)
                    continue;

                var name = ReadName(channel, deviceIndex, ct);
                if (name != SupportedDeviceName)
                {
                    AppLog.Write($"Skipping device: PID {channel.ProductId:X4}, devIdx {deviceIndex:X2}, name {(name is null ? "unreadable" : $"\"{name}\"")}");
                    continue;
                }
                AppLog.Write($"Device found: PID {channel.ProductId:X4}, devIdx {deviceIndex:X2}, battery feature index {featureIndex:X2}");
                return new Target(channel, deviceIndex, featureIndex);
            }
            catch (Exception ex) when (ex is IOException or TimeoutException)
            {
                break; // the channel itself failed (e.g. unplugged): the remaining indexes would fail the same way
            }
        }
        return null;
    }

    private static byte? FindBatteryFeatureIndex(HidPpChannel channel, byte deviceIndex, CancellationToken ct)
    {
        if (RequestDuringDiscovery(channel, HidPpMessage.BuildPing(deviceIndex), ct) is null)
            return null;
        return FindFeatureIndex(channel, deviceIndex, UnifiedBattery.FeatureId, ct);
    }

    /// <summary>Asks the root feature for the index of <paramref name="featureId"/>; null if unsupported or unanswered.</summary>
    private static byte? FindFeatureIndex(HidPpChannel channel, byte deviceIndex, ushort featureId, CancellationToken ct)
    {
        var response = RequestDuringDiscovery(channel, HidPpMessage.BuildGetFeature(deviceIndex, featureId), ct);
        var featureIndex = response is null ? (byte)0 : HidPpMessage.ParamsOf(response)[0];
        return featureIndex == 0 ? null : featureIndex;
    }

    /// <summary>Reads the DeviceName feature (0x0005); null if unsupported or unreadable.</summary>
    private static string? ReadName(HidPpChannel channel, byte deviceIndex, CancellationToken ct)
    {
        if (FindFeatureIndex(channel, deviceIndex, DeviceName.FeatureId, ct) is not { } featureIndex)
            return null;

        var countResponse = RequestDuringDiscovery(channel, HidPpMessage.BuildRequest(deviceIndex, featureIndex, DeviceName.GetCountFunction, HidPpMessage.SoftwareId), ct);
        if (countResponse is null || HidPpMessage.ParamsOf(countResponse)[0] == 0)
            return null;

        var length = DeviceName.LimitLength(HidPpMessage.ParamsOf(countResponse)[0]);
        var chunks = DeviceName.ReadChunks(length, position =>
        {
            var request = HidPpMessage.BuildRequest(deviceIndex, featureIndex, DeviceName.GetNameFunction, HidPpMessage.SoftwareId, position);
            return RequestDuringDiscovery(channel, request, ct) is { } chunk ? HidPpMessage.ParamsOf(chunk) : null;
        });
        return DeviceName.IsComplete(length, chunks) ? DeviceName.Assemble(length, chunks) : null;
    }

    private static byte[]? RequestDuringDiscovery(HidPpChannel channel, byte[] request, CancellationToken ct) =>
        channel.Exchange(request, DiscoveryTimeout, ct).Response;
}
