namespace LogitechBattery.Battery;

/// <summary>Chooses the one device the tray icon shows.</summary>
public static class TrayDeviceChoice
{
    /// <summary>
    /// The saved device when it was read; a not-found entry for it when it was not (the tray never switches to another device
    /// by itself); the first device when none is saved. Null when there is nothing to show.
    /// </summary>
    public static DeviceReading? Pick(
        IReadOnlyList<DeviceReading> readings,
        DeviceId? saved,
        IReadOnlyDictionary<DeviceId, string> savedNames,
        DateTimeOffset now)
    {
        if (saved is null)
            return readings.FirstOrDefault();

        return readings.FirstOrDefault(reading => reading.Device.Id == saved) ?? NotFound(saved, savedNames, now);
    }

    private static DeviceReading NotFound(DeviceId id, IReadOnlyDictionary<DeviceId, string> savedNames, DateTimeOffset now)
    {
        var name = savedNames.GetValueOrDefault(id, string.Empty);
        return new DeviceReading(new DeviceInfo(id, name), new BatteryStatus(ConnectionState.NotFound, null, ChargingState.Unknown, now));
    }
}
