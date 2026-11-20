namespace LogitechBattery.Battery;

/// <summary>Tells whether every device the user is expected to have is connected, so follow-up reads can stop.</summary>
public static class ExpectedDevices
{
    /// <summary>
    /// Expected devices are the remembered ones plus every device in <paramref name="readings"/>. A remembered device that
    /// is not in the readings counts as not connected. With no expected device (first run, mouse still off) this is false,
    /// so the follow-up goes on until a device shows up.
    /// </summary>
    public static bool AllConnected(IReadOnlyCollection<DeviceId> rememberedIds, IReadOnlyList<DeviceReading> readings)
    {
        var connectedIds = readings
            .Where(reading => !reading.Device.IsPlaceholder)
            .Where(reading => reading.Status?.Connection == ConnectionState.Connected)
            .Select(reading => reading.Device.Id)
            .ToHashSet();
        var expectedIds = readings
            .Where(reading => !reading.Device.IsPlaceholder)
            .Select(reading => reading.Device.Id)
            .Concat(rememberedIds)
            .ToHashSet();

        return expectedIds.Count > 0 && expectedIds.All(connectedIds.Contains);
    }
}
