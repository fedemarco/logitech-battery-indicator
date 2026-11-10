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
            if (status is not null && AlertsOf(device).Update(status, settings) is { } alert)
                alerts.Add(new DeviceAlert(displayNames[device.Id], alert, status.Percent));
        }

        return new DeviceTrackerUpdate(alerts, RememberNames(realReadings));
    }

    private BatteryAlerts AlertsOf(DeviceInfo device)
    {
        if (!_alertsByDevice.TryGetValue(device.Id, out var deviceAlerts))
            _alertsByDevice[device.Id] = deviceAlerts = new BatteryAlerts();
        return deviceAlerts;
    }

    private IReadOnlyDictionary<DeviceId, string>? RememberNames(List<DeviceReading> realReadings)
    {
        var changed = false;
        foreach (var (device, _) in realReadings)
            changed |= RememberName(device);
        return changed ? new Dictionary<DeviceId, string>(_knownNames) : null;
    }

    // The settings file saves the cleaned name, so compare that form or the name would be saved again at every start.
    private bool RememberName(DeviceInfo device)
    {
        var cleanName = SettingsFile.CleanName(device.Name);
        if (_knownNames.TryGetValue(device.Id, out var knownName) && knownName == cleanName)
            return false;
        _knownNames[device.Id] = cleanName;
        return true;
    }
}
