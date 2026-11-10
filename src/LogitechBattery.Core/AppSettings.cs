using LogitechBattery.Battery;

namespace LogitechBattery;

/// <summary>Everything settings.ini holds: the alert settings, the device the tray shows and the name of every device seen.</summary>
public sealed record AppSettings(
    AlertSettings Alerts,
    DeviceId? TrayDevice,
    IReadOnlyDictionary<DeviceId, string> DeviceNames)
{
    public static AppSettings Default { get; } = new(AlertSettings.Default, TrayDevice: null, new Dictionary<DeviceId, string>());
}
