using LogitechBattery.Battery;

namespace LogitechBattery.Tray;

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

    // Devices seen earlier in this session stay known after they go away; listing them would show ghosts. The tray device stays
    // listed even then (also a saved device that was not read), so the user can see it and switch away from it.
    // The placeholder is not a device.
    private static List<DeviceReading> ListedDevices(IReadOnlyList<DeviceReading> readings, DeviceReading tray)
    {
        var listed = readings
            .Where(reading => !reading.Device.IsPlaceholder)
            .Where(reading => AnswersNow(reading.Status) || reading.Device.Id == tray.Device.Id)
            .ToList();
        var trayIsMissing = !tray.Device.IsPlaceholder && listed.All(reading => reading.Device.Id != tray.Device.Id);
        if (trayIsMissing)
            listed.Add(tray);
        return listed;
    }

    // A failed read (no status) still means the device answered.
    private static bool AnswersNow(BatteryStatus? status) =>
        status?.Connection is not (ConnectionState.Asleep or ConnectionState.NotFound);

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
