using LogitechBattery.Battery;
using LogitechBattery.Logging;

namespace LogitechBattery.Devices;

/// <summary>Turns the statuses of one read into one reading per known device, and logs each device's connection state once per change.</summary>
internal sealed class DeviceReadingsBuilder
{
    private readonly Dictionary<DeviceId, ConnectionState> _lastStates = [];

    /// <summary>
    /// One reading per known device, in first-seen order; a device that did not answer is Asleep or NotFound. The placeholder
    /// comes last, and only while an open channel has no known device on it: that receiver may hold a device that never answered.
    /// </summary>
    public IReadOnlyList<DeviceReading> Build(
        KnownDevices known,
        IReadOnlyDictionary<DeviceId, BatteryStatus?> statuses,
        IReadOnlySet<string> openChannelKeys,
        DateTimeOffset now)
    {
        if (known.IsEmpty)
            return [PlaceholderReading(MissingDeviceState(openChannelKeys), now)];

        var readings = known.InFirstSeenOrder.Select(device => ReadingOf(device, known, statuses, openChannelKeys, now)).ToList();
        if (known.AnyChannelWithoutDevice(openChannelKeys))
            readings.Add(PlaceholderReading(ConnectionState.Asleep, now));
        return readings;
    }

    private DeviceReading PlaceholderReading(ConnectionState state, DateTimeOffset now) =>
        new(DeviceInfo.NoDeviceYet, NoReading(DeviceInfo.NoDeviceYet, state, now));

    private DeviceReading ReadingOf(
        DeviceInfo device,
        KnownDevices known,
        IReadOnlyDictionary<DeviceId, BatteryStatus?> statuses,
        IReadOnlySet<string> openChannelKeys,
        DateTimeOffset now)
    {
        if (!statuses.TryGetValue(device.Id, out var status))
        {
            var state = known.StateWhenNotAnswering(device, openChannelKeys);
            return new DeviceReading(device, NoReading(device, state, now));
        }
        if (status is not null)
            LogState(device, status.Connection);
        return new DeviceReading(device, status);
    }

    // A receiver without an answering mouse means the mouse sleeps or is off.
    // shortcut: any open HID++ device counts as that receiver, so another Logitech device alone reads as Asleep; the placeholder shows
    // only while an open channel has no device with Unified Battery found on it.
    // shortcut: an open HID++ channel (receiver or cable) whose only device has no Unified Battery (skipped) counts as having no known
    // device, so an unread saved tray device shows Asleep instead of Not found; upgrade if a user reports it.
    private static ConnectionState MissingDeviceState(IReadOnlySet<string> openChannelKeys) =>
        openChannelKeys.Count > 0 ? ConnectionState.Asleep : ConnectionState.NotFound;

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
}
