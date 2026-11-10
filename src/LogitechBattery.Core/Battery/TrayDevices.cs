namespace LogitechBattery.Battery;

/// <summary>One entry of the "Show in tray" menu.</summary>
public sealed record TrayDeviceItem(DeviceId Id, string Text, bool IsTray);

/// <summary>What the tray shows after a round of readings: the chosen device, the other devices and the menu entries to choose from.</summary>
public sealed record TrayDevices(
    DeviceReading Tray,
    string TrayName,
    IReadOnlyList<(string Name, BatteryStatus? Status)> Others,
    IReadOnlyList<TrayDeviceItem> Items)
{
    private const string ReadFailedText = "Battery read failed";
    private const string TextSeparator = " · ";

    public string StatusLine => Tray.Status?.ToDisplayString() ?? ReadFailedText;

    public string Headline => $"{TrayName}{TextSeparator}{StatusLine}";

    public static TrayDevices From(
        IReadOnlyList<DeviceReading> readings,
        DeviceId? saved,
        IReadOnlyDictionary<DeviceId, string> savedNames,
        DateTimeOffset now)
    {
        var tray = TrayDeviceChoice.Pick(readings, saved, savedNames, now) ?? new DeviceReading(DeviceInfo.NoDeviceYet, null);
        var listed = ListedDevices(readings, tray);
        var names = DeviceNames.Display(listed.Select(reading => reading.Device).ToList());

        return new TrayDevices(
            tray,
            names.GetValueOrDefault(tray.Device.Id, tray.Device.Name),
            listed.Where(reading => reading.Device.Id != tray.Device.Id).Select(reading => (names[reading.Device.Id], reading.Status)).ToList(),
            listed.Select(reading => new TrayDeviceItem(
                reading.Device.Id,
                $"{names[reading.Device.Id]}{TextSeparator}{MenuStatusText(reading.Status)}",
                reading.Device.Id == tray.Device.Id)).ToList());
    }

    // The placeholder is not a device; a saved device that was not read is listed so the user can see it and switch away from it.
    private static List<DeviceReading> ListedDevices(IReadOnlyList<DeviceReading> readings, DeviceReading tray)
    {
        var listed = readings.Where(reading => reading.Device.Id != DeviceInfo.NoDeviceYet.Id).ToList();
        var trayIsMissing = tray.Device.Id != DeviceInfo.NoDeviceYet.Id && listed.All(reading => reading.Device.Id != tray.Device.Id);
        if (trayIsMissing)
            listed.Add(tray);
        return listed;
    }

    private static string MenuStatusText(BatteryStatus? status) => status switch
    {
        null => "read failed",
        { Connection: ConnectionState.Connected, Percent: { } percent } => $"{percent}%",
        _ => status.ToDisplayString().ToLowerInvariant(),
    };
}
