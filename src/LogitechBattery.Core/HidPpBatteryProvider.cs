using HidSharp;
using LogitechBattery.Battery;
using LogitechBattery.HidPp;

namespace LogitechBattery;

/// <summary>Reads the mouse battery over HID++ 2.0 (Unified Battery), via Lightspeed receiver or USB cable.</summary>
public sealed class HidPpBatteryProvider : IBatteryProvider
{
    private const int StreamReadTimeoutMs = 200;
    private const int StreamWriteTimeoutMs = 200;
    private static readonly TimeSpan DiscoveryTimeout = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(1);

    // shortcut: single supported model until device selection (multiple devices) is implemented
    private const string SupportedDeviceName = "PRO X2 SUPERSTRIKE";

    private sealed record Target(string Path, byte DeviceIndex, byte FeatureIndex);

    /// <summary>What a request got back: the response report, or the code of a HID++ 2.0 error, or neither (error 1.0 / timeout).</summary>
    private readonly record struct Answer(byte[]? Response, byte? ErrorCode)
    {
        public static readonly Answer Nothing = new(null, null);
    }

    private Target? _target;
    private ConnectionState? _lastState;
    private volatile bool _rediscover;

    /// <summary>Raised (on a background thread) when USB HID devices are added or removed, e.g. cable plugged in.</summary>
    public event Action? DevicesChanged;

    public HidPpBatteryProvider() => DeviceList.Local.Changed += OnUsbDevicesChanged;

    public Task<BatteryStatus> ReadAsync(CancellationToken cancellationToken = default) => Task.Run(() => Read(cancellationToken), cancellationToken);

    private void OnUsbDevicesChanged(object? sender, DeviceListChangedEventArgs e)
    {
        AppLog.Write("USB devices changed");
        _rediscover = true; // the best route may have changed (cable preferred over receiver)
        DevicesChanged?.Invoke();
    }

    private BatteryStatus Read(CancellationToken ct)
    {
        try
        {
            return ReadCore(retry: true, ct);
        }
        catch (Exception ex)
        {
            AppLog.Write($"Battery read failed: {ex}");
            throw;
        }
    }

    private BatteryStatus ReadCore(bool retry, CancellationToken ct)
    {
        var now = DateTimeOffset.Now;
        if (_rediscover)
        {
            _rediscover = false;
            ForgetTarget();
        }

        _target ??= Discover(ct);
        if (_target is not { } target)
            return NoReading(MissingTargetState(), now);

        var device = DeviceList.Local.GetHidDevices(HidPpDevices.LogitechVid).FirstOrDefault(d => d.DevicePath == target.Path);
        if (device is null || !device.TryOpen(out var stream))
            return Lost(target, now, retry, ct);

        return ReadStatus(stream, target, now, retry, ct);
    }

    private BatteryStatus ReadStatus(HidStream stream, Target target, DateTimeOffset now, bool retry, CancellationToken ct)
    {
        try
        {
            using (stream)
            {
                ConfigureTimeouts(stream);
                var request = HidPpMessage.BuildRequest(target.DeviceIndex, target.FeatureIndex, UnifiedBattery.GetStatusFunction, HidPpMessage.SoftwareId);
                var answer = Exchange(stream, request, ResponseTimeout, ct);

                if (answer.ErrorCode is { } errorCode)
                {
                    // The device answered with an error: not asleep.
                    AppLog.Write($"getStatus failed with HID++ 2.0 error 0x{errorCode:X2}");
                    ForgetTarget(); // indexes may be stale (e.g. re-paired): rediscover next time
                    throw new InvalidOperationException($"Battery status request failed (HID++ error 0x{errorCode:X2}).");
                }
                if (answer.Response is not { } response)
                {
                    ForgetTarget(); // rediscover next time (the mouse may be paired to another slot)
                    return NoReading(ConnectionState.Asleep, now);
                }

                var status = UnifiedBattery.ParseStatus(HidPpMessage.ParamsOf(response), now);
                LogState(ConnectionState.Connected);
                return status;
            }
        }
        catch (Exception ex) when (ex is IOException or TimeoutException)
        {
            return Lost(target, now, retry, ct); // unplugged mid-read
        }
    }

    // Rediscover once right away (e.g. cable unplugged while the receiver is still there).
    private BatteryStatus Lost(Target target, DateTimeOffset now, bool retry, CancellationToken ct)
    {
        AppLog.Write($"Device lost: {target.Path}");
        ForgetTarget();
        return retry ? ReadCore(retry: false, ct) : NoReading(ConnectionState.NotFound, now);
    }

    private void ForgetTarget() => _target = null;

    // A receiver without an answering mouse means the mouse sleeps or is off.
    private static ConnectionState MissingTargetState() =>
        HidPpDevices.Find().Count > 0 ? ConnectionState.Asleep : ConnectionState.NotFound;

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

    /// <summary>Pass 1: the cable's device index; pass 2: the receiver slots. First HID++ 2.0 device with Unified Battery wins.</summary>
    private static Target? Discover(CancellationToken ct)
    {
        var devices = HidPpDevices.Find();
        foreach (var deviceIndexes in new[] { HidPpMessage.CableDeviceIndexes, HidPpMessage.ReceiverDeviceIndexes })
        {
            foreach (var device in devices)
            {
                if (FindTarget(device, deviceIndexes, ct) is { } target)
                    return target;
            }
        }
        return null;
    }

    private static Target? FindTarget(HidPpDevice device, byte[] deviceIndexes, CancellationToken ct)
    {
        if (!device.LongCollection.TryOpen(out var stream))
            return null;

        using (stream)
        {
            ConfigureTimeouts(stream);
            foreach (var deviceIndex in deviceIndexes)
            {
                try
                {
                    if (FindBatteryFeatureIndex(stream, deviceIndex, ct) is not { } featureIndex)
                        continue;

                    var name = ReadName(stream, deviceIndex, ct);
                    if (name != SupportedDeviceName)
                    {
                        AppLog.Write($"Skipping device: PID {device.LongCollection.ProductID:X4}, devIdx {deviceIndex:X2}, name {(name is null ? "unreadable" : $"\"{name}\"")}");
                        continue;
                    }
                    AppLog.Write($"Device found: PID {device.LongCollection.ProductID:X4}, devIdx {deviceIndex:X2}, battery feature index {featureIndex:X2}");
                    return new Target(device.LongCollection.DevicePath, deviceIndex, featureIndex);
                }
                catch (Exception ex) when (ex is IOException or TimeoutException)
                {
                    break; // the stream itself failed (e.g. unplugged): the remaining indexes would fail the same way
                }
            }
        }
        return null;
    }

    private static void ConfigureTimeouts(HidStream stream)
    {
        stream.ReadTimeout = StreamReadTimeoutMs;
        stream.WriteTimeout = StreamWriteTimeoutMs;
    }

    private static byte? FindBatteryFeatureIndex(HidStream stream, byte deviceIndex, CancellationToken ct)
    {
        if (RequestDuringDiscovery(stream, HidPpMessage.BuildPing(deviceIndex), ct) is null)
            return null;
        return FindFeatureIndex(stream, deviceIndex, UnifiedBattery.FeatureId, ct);
    }

    /// <summary>Asks the root feature for the index of <paramref name="featureId"/>; null if unsupported or unanswered.</summary>
    private static byte? FindFeatureIndex(HidStream stream, byte deviceIndex, ushort featureId, CancellationToken ct)
    {
        var response = RequestDuringDiscovery(stream, HidPpMessage.BuildGetFeature(deviceIndex, featureId), ct);
        var featureIndex = response is null ? (byte)0 : HidPpMessage.ParamsOf(response)[0];
        return featureIndex == 0 ? null : featureIndex;
    }

    /// <summary>Reads the DeviceName feature (0x0005); null if unsupported or unreadable.</summary>
    private static string? ReadName(HidStream stream, byte deviceIndex, CancellationToken ct)
    {
        if (FindFeatureIndex(stream, deviceIndex, DeviceName.FeatureId, ct) is not { } featureIndex)
            return null;

        var countResponse = RequestDuringDiscovery(stream, HidPpMessage.BuildRequest(deviceIndex, featureIndex, DeviceName.GetCountFunction, HidPpMessage.SoftwareId), ct);
        if (countResponse is null || HidPpMessage.ParamsOf(countResponse)[0] == 0)
            return null;

        var length = DeviceName.LimitLength(HidPpMessage.ParamsOf(countResponse)[0]);
        var chunks = DeviceName.ReadChunks(length, position =>
        {
            var request = HidPpMessage.BuildRequest(deviceIndex, featureIndex, DeviceName.GetNameFunction, HidPpMessage.SoftwareId, position);
            return RequestDuringDiscovery(stream, request, ct) is { } chunk ? HidPpMessage.ParamsOf(chunk) : null;
        });
        return DeviceName.IsComplete(length, chunks) ? DeviceName.Assemble(length, chunks) : null;
    }

    private static byte[]? RequestDuringDiscovery(HidStream stream, byte[] request, CancellationToken ct) =>
        Exchange(stream, request, DiscoveryTimeout, ct).Response;

    /// <summary>Sends <paramref name="request"/> and waits for the matching response, a HID++ 2.0 error or the timeout.</summary>
    // shortcut: only the long collection is read, so HID++ 1.0 errors (short reports on another collection) show up as a timeout; revisit if the sleeping-mouse behavior needs distinguishing.
    private static Answer Exchange(HidStream stream, byte[] request, TimeSpan timeout, CancellationToken ct)
    {
        stream.Write(request);
        var buffer = new byte[stream.Device.GetMaxInputReportLength()];
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            int length;
            try { length = stream.Read(buffer, 0, buffer.Length); }
            catch (TimeoutException) { continue; }
            var report = buffer[..length];
            var reply = HidPpMessage.Classify(request, report);
            switch (reply.Kind)
            {
                case HidPpReplyKind.Response:
                    return new Answer(report, null);
                case HidPpReplyKind.Error20:
                    return new Answer(null, reply.ErrorCode);
                case HidPpReplyKind.Error10:
                    return Answer.Nothing;
                case HidPpReplyKind.None:
                    break; // notification or answer to another request: keep waiting
            }
        }
        return Answer.Nothing;
    }
}
