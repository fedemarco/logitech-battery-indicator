using LogitechBattery.Alerts;
using LogitechBattery.Battery;

namespace LogitechBattery.Settings;

/// <summary>Everything settings.ini holds: the alert settings, the device the tray shows, the name of every device seen and whether the app checks for updates.</summary>
public sealed record AppSettings(
    AlertSettings Alerts,
    DeviceId? TrayDevice,
    IReadOnlyDictionary<DeviceId, string> DeviceNames,
    bool CheckForUpdates = false)
{
    public static AppSettings Default { get; } = new(AlertSettings.Default, TrayDevice: null, new Dictionary<DeviceId, string>());
}
