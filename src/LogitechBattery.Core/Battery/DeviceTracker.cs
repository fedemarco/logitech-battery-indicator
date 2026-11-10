namespace LogitechBattery.Battery;

/// <summary>A notification to show: which device, what happened and at what charge.</summary>
public sealed record DeviceAlert(string DeviceName, BatteryAlert Alert, int? Percent);

/// <summary>What one round of readings produced. <paramref name="NamesToSave"/> is null unless a device is new or renamed.</summary>
public sealed record DeviceTrackerUpdate(IReadOnlyList<DeviceAlert> Alerts, IReadOnlyDictionary<DeviceId, string>? NamesToSave);

/// <summary>Keeps one <see cref="BatteryAlerts"/> per device, so every device notifies on its own, and the names of all devices seen.</summary>
public sealed class DeviceTracker(IReadOnlyDictionary<DeviceId, string> savedNames)
{
    private readonly Dictionary<DeviceId, BatteryAlerts> _alertsByDevice = [];
    private readonly Dictionary<DeviceId, string> _knownNames = new(savedNames);

    public DeviceTrackerUpdate Update(IReadOnlyList<DeviceReading> readings, AlertSettings settings)
    {
        var realReadings = readings.Where(reading => reading.Device.Id != DeviceInfo.NoDeviceYet.Id).ToList();
        var displayNames = DeviceNames.Display(realReadings.Select(reading => reading.Device).ToList());

        var alerts = new List<DeviceAlert>();
        foreach (var (device, status) in realReadings)
        {
            if (status is null)
                continue;
            if (!_alertsByDevice.TryGetValue(device.Id, out var deviceAlerts))
                _alertsByDevice[device.Id] = deviceAlerts = new BatteryAlerts();
            if (deviceAlerts.Update(status, settings) is { } alert)
                alerts.Add(new DeviceAlert(displayNames[device.Id], alert, status.Percent));
        }

        return new DeviceTrackerUpdate(alerts, RememberNames(realReadings));
    }

    private IReadOnlyDictionary<DeviceId, string>? RememberNames(List<DeviceReading> realReadings)
    {
        var changed = false;
        foreach (var (device, _) in realReadings)
        {
            if (_knownNames.TryGetValue(device.Id, out var knownName) && knownName == device.Name)
                continue;
            _knownNames[device.Id] = device.Name;
            changed = true;
        }
        return changed ? new Dictionary<DeviceId, string>(_knownNames) : null;
    }
}
