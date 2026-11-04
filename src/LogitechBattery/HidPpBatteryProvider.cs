using HidSharp;
using LogitechBattery.Battery;
using LogitechBattery.HidPp;

namespace LogitechBattery;

/// <summary>Reads the mouse battery over HID++ 2.0 (Unified Battery), via Lightspeed receiver or USB cable.</summary>
internal sealed class HidPpBatteryProvider : IBatteryProvider
{
    private const byte SwId = 0x0A;
    private const int WriteTimeoutMs = 200;
    private static readonly TimeSpan DiscoveryTimeout = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(1);

    // shortcut: single supported model until device selection (multiple devices) is implemented
    private const string SupportedDeviceName = "PRO X2 SUPERSTRIKE";

    private sealed record Target(string Path, byte DeviceIndex, byte FeatureIndex);

    private Target? _target;
    private ConnectionState? _lastState;
    private volatile bool _rediscover;

    /// <summary>Raised (on a background thread) when USB HID devices are added or removed, e.g. cable plugged in.</summary>
    public event Action? DevicesChanged;

    public HidPpBatteryProvider() => DeviceList.Local.Changed += (_, _) =>
    {
        AppLog.Write("USB devices changed");
        _rediscover = true; // the best route may have changed (cable preferred over receiver)
        DevicesChanged?.Invoke();
    };

    public Task<BatteryStatus> ReadAsync(CancellationToken cancellationToken = default) => Task.Run(Read, cancellationToken);

    private BatteryStatus Read()
    {
        try
        {
            return ReadCore(retry: true);
        }
        catch (Exception ex)
        {
            AppLog.Write($"Battery read failed: {ex}");
            throw;
        }
    }

    private BatteryStatus ReadCore(bool retry)
    {
        var now = DateTimeOffset.Now;
        if (_rediscover)
        {
            _rediscover = false;
            _target = null;
        }
        _target ??= Discover();
        if (_target is not { } target)
            // A receiver without an answering mouse means the mouse sleeps or is off.
            return Result(HidPpDevices.Find().Count > 0 ? ConnectionState.Asleep : ConnectionState.NotFound, now);

        var device = DeviceList.Local.GetHidDevices(HidPpDevices.LogitechVid).FirstOrDefault(d => d.DevicePath == target.Path);
        if (device is null || !device.TryOpen(out var stream))
            return Lost(target, now, retry);

        try
        {
            using (stream)
            {
                stream.ReadTimeout = 200;
                stream.WriteTimeout = WriteTimeoutMs;
                var request = HidPpMessage.BuildRequest(target.DeviceIndex, target.FeatureIndex, UnifiedBattery.GetStatusFunction, SwId);
                var response = Exchange(stream, request, ReadTimeout);
                if (response is null)
                    return Result(ConnectionState.Asleep, now);
                var status = UnifiedBattery.ParseStatus(response.AsSpan(4, 4), now);
                LogState(ConnectionState.Connected);
                return status;
            }
        }
        catch (Exception ex) when (ex is IOException or TimeoutException)
        {
            return Lost(target, now, retry); // unplugged mid-read
        }
    }

    // Rediscover once right away (e.g. cable unplugged while the receiver is still there).
    private BatteryStatus Lost(Target target, DateTimeOffset now, bool retry)
    {
        AppLog.Write($"Device lost: {target.Path}");
        _target = null;
        return retry ? ReadCore(retry: false) : Result(ConnectionState.NotFound, now);
    }

    private BatteryStatus Result(ConnectionState state, DateTimeOffset now)
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

    /// <summary>Pass 1: devIdx 0xFF (cable); pass 2: devIdx 1..6 (receiver slots). First HID++ 2.0 device with Unified Battery wins.</summary>
    private static Target? Discover()
    {
        var devices = HidPpDevices.Find();
        var indexes = new[] { new byte[] { 0xFF }, new byte[] { 1, 2, 3, 4, 5, 6 } };
        foreach (var pass in indexes)
        {
            foreach (var device in devices)
            {
                if (!device.Long.TryOpen(out var stream))
                    continue;
                using (stream)
                {
                    stream.ReadTimeout = 200;
                    stream.WriteTimeout = WriteTimeoutMs;
                    foreach (var index in pass)
                    {
                        try
                        {
                            if (TryFindFeature(stream, index) is { } feature)
                            {
                                var name = ReadName(stream, index);
                                if (name != SupportedDeviceName)
                                {
                                    AppLog.Write($"Skipping device: PID {device.Long.ProductID:X4}, devIdx {index:X2}, name {(name is null ? "unreadable" : $"\"{name}\"")}");
                                    continue;
                                }
                                AppLog.Write($"Device found: PID {device.Long.ProductID:X4}, devIdx {index:X2}, battery feature index {feature:X2}");
                                return new Target(device.Long.DevicePath, index, feature);
                            }
                        }
                        catch (Exception ex) when (ex is IOException or TimeoutException) { break; }
                    }
                }
            }
        }
        return null;
    }

    private static byte? TryFindFeature(HidStream stream, byte index)
    {
        // Root feature (index 0): fn1 getProtocolVersion as ping, fn0 getFeature.
        if (Exchange(stream, HidPpMessage.BuildRequest(index, 0, 1, SwId, 0, 0, 0x5A), DiscoveryTimeout) is null)
            return null;
        var r = Exchange(stream, HidPpMessage.BuildRequest(index, 0, 0, SwId, UnifiedBattery.FeatureId >> 8, UnifiedBattery.FeatureId & 0xFF), DiscoveryTimeout);
        return r is { } && r[4] != 0 ? r[4] : null;
    }

    /// <summary>Reads the DeviceName feature (0x0005); null if unsupported or unreadable.</summary>
    private static string? ReadName(HidStream stream, byte index)
    {
        var r = Exchange(stream, HidPpMessage.BuildRequest(index, 0, 0, SwId, DeviceName.FeatureId >> 8, DeviceName.FeatureId & 0xFF), DiscoveryTimeout);
        if (r is not { Length: > 4 } || r[4] == 0)
            return null;
        var feature = r[4];
        if (Exchange(stream, HidPpMessage.BuildRequest(index, feature, DeviceName.GetCountFunction, SwId), DiscoveryTimeout) is not { Length: > 4 } count || count[4] == 0)
            return null;
        var length = Math.Min((int)count[4], 64);
        var chunks = new List<byte[]>();
        for (var pos = 0; pos < length; pos += 16)
        {
            if (Exchange(stream, HidPpMessage.BuildRequest(index, feature, DeviceName.GetNameFunction, SwId, (byte)pos), DiscoveryTimeout) is not { Length: > 4 } chunk)
                return null;
            chunks.Add(chunk[4..Math.Min(chunk.Length, 20)]);
        }
        return DeviceName.Assemble(length, chunks);
    }

    /// <summary>
    /// Sends <paramref name="request"/> and returns the matching response, or null on error/timeout.
    /// shortcut: only the long collection is read, so HID++ 1.0 errors (short reports on another collection) show up as a timeout; revisit if the sleeping-mouse behavior needs distinguishing.
    /// </summary>
    private static byte[]? Exchange(HidStream stream, byte[] request, TimeSpan timeout)
    {
        stream.Write(request);
        var buffer = new byte[stream.Device.GetMaxInputReportLength()];
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            int n;
            try { n = stream.Read(buffer, 0, buffer.Length); }
            catch (TimeoutException) { continue; }
            var report = buffer[..n];
            switch (HidPpMessage.Classify(request, report).Kind)
            {
                case HidPpReplyKind.Response:
                    return report;
                case HidPpReplyKind.Error20 or HidPpReplyKind.Error10:
                    return null;
            }
        }
        return null;
    }
}
