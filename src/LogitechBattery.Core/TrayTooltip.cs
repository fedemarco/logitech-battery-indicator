using LogitechBattery.Battery;

namespace LogitechBattery;

/// <summary>The text of the tray icon's tooltip, one per line: the shown device and its status, the estimate, the other devices and the app name with version.</summary>
public static class TrayTooltip
{
    /// <summary>NotifyIcon.MaxTextSize in dotnet/winforms (szTip holds 128 with the terminator); a longer text makes NotifyIcon.Text throw.</summary>
    public const int MaxLength = 127;

    private const string OtherDevicesSeparator = " · ";

    /// <summary>The text is cut at <see cref="MaxLength"/> from the end, so the version line goes first, then the other devices.</summary>
    public static string Build(
        string deviceName,
        string? statusLine,
        string? remainingText,
        IReadOnlyList<OtherDevice> otherDevices,
        string displayVersion)
    {
        var lines = new[]
        {
            statusLine is null ? null : $"{deviceName}: {statusLine}",
            remainingText,
            OtherDevicesLine(otherDevices),
            $"Logitech Battery {displayVersion}",
        }.OfType<string>();
        var text = string.Join('\n', lines);
        return text.Length > MaxLength ? text[..MaxLength] : text;
    }

    private static string? OtherDevicesLine(IReadOnlyList<OtherDevice> otherDevices) =>
        otherDevices.Count == 0
            ? null
            : string.Join(OtherDevicesSeparator, otherDevices.Select(device => $"{device.Name}: {BatteryStatusText.Short(device.Status)}"));
}
