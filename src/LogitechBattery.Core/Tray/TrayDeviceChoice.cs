using LogitechBattery.Battery;

namespace LogitechBattery.Tray;

/// <summary>Chooses the one device the tray icon shows.</summary>
public static class TrayDeviceChoice
{
    /// <summary>
    /// The saved device when it was read; an entry for it when it was not (the tray never switches to another device by itself),
    /// asleep while an open receiver has no known device (the placeholder reading says so), not found otherwise; the first device when none is saved.
    /// Null when there is nothing to show.
    /// </summary>
    public static DeviceReading? Pick(
        IReadOnlyList<DeviceReading> readings,
        DeviceId? saved,
        IReadOnlyDictionary<DeviceId, string> savedNames,
        DateTimeOffset now)
    {
        if (saved is null)
            return readings.FirstOrDefault();

        var savedReading = readings.FirstOrDefault(reading => reading.Device.Id == saved);
        return savedReading ?? NotRead(saved, savedNames, StateWhileAReceiverHasNoKnownDevice(readings), now);
    }

    /// <summary>
    /// The device to save as the tray device: the first real device, when none is saved yet. Saving it keeps the tray from
    /// switching by itself to another device in a later session, when the devices are read in another order. Null otherwise.
    /// </summary>
    public static DeviceId? FirstPickToSave(IReadOnlyList<DeviceReading> readings, DeviceId? saved)
    {
        if (saved is not null)
            return null;

        return readings.FirstOrDefault(reading => !reading.Device.IsPlaceholder)?.Device.Id;
    }

    // The placeholder carries the receiver state while no device answered yet (for example when the mouse sleeps at app start),
    // and is Asleep while an open receiver has no known device. Without it, every receiver has a known device, so a saved device
    // that was not read is not found.
    private static ConnectionState StateWhileAReceiverHasNoKnownDevice(IReadOnlyList<DeviceReading> readings) =>
        readings.FirstOrDefault(reading => reading.Device.IsPlaceholder)?.Status?.Connection ?? ConnectionState.NotFound;

    private static DeviceReading NotRead(DeviceId id, IReadOnlyDictionary<DeviceId, string> savedNames, ConnectionState state, DateTimeOffset now)
    {
        var name = savedNames.GetValueOrDefault(id, string.Empty);
        return new DeviceReading(new DeviceInfo(id, name), new BatteryStatus(state, null, ChargingState.Unknown, now));
    }
}
