namespace LogitechBattery.Battery;

/// <summary>One entry of the "Show in tray" menu.</summary>
public sealed record TrayDeviceItem(DeviceId Id, string Text, bool IsTray);

/// <summary>A device shown next to the tray device, in the tooltip.</summary>
public sealed record OtherDevice(string Name, BatteryStatus? Status);

/// <summary>What the tray shows after a round of readings: the chosen device, the other devices and the menu entries to choose from.</summary>
public sealed record TrayDevices(
    DeviceReading Tray,
    string TrayName,
    IReadOnlyList<OtherDevice> Others,
    IReadOnlyList<TrayDeviceItem> Items)
{
    private const string ReadFailedText = "Battery read failed";
    private const string TextSeparator = " · ";

    public string StatusLine => Tray.Status?.ToDisplayString() ?? ReadFailedText;

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
            OthersOf(listed, tray, names),
            MenuItemsOf(listed, tray, names));
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

    private static List<OtherDevice> OthersOf(List<DeviceReading> listed, DeviceReading tray, IReadOnlyDictionary<DeviceId, string> names) =>
        listed
            .Where(reading => reading.Device.Id != tray.Device.Id)
            .Select(reading => new OtherDevice(names[reading.Device.Id], reading.Status))
            .ToList();

    private static List<TrayDeviceItem> MenuItemsOf(List<DeviceReading> listed, DeviceReading tray, IReadOnlyDictionary<DeviceId, string> names) =>
        listed.Select(reading => MenuItemOf(reading, tray, names)).ToList();

    private static TrayDeviceItem MenuItemOf(DeviceReading reading, DeviceReading tray, IReadOnlyDictionary<DeviceId, string> names)
    {
        var statusText = BatteryStatusText.Short(reading.Status).ToLowerInvariant(); // lower case after the name: "G915 · read failed"
        return new TrayDeviceItem(reading.Device.Id, $"{names[reading.Device.Id]}{TextSeparator}{statusText}", reading.Device.Id == tray.Device.Id);
    }
}
