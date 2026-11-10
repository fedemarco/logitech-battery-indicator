using LogitechBattery.Battery;

namespace LogitechBattery.Tests;

public class TrayDevicesTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch.AddDays(1);
    private static readonly DeviceId MouseId = DeviceId.Parse("11111111");
    private static readonly DeviceId KeyboardId = DeviceId.Parse("22222222");
    private static readonly IReadOnlyDictionary<DeviceId, string> NoNames = new Dictionary<DeviceId, string>();

    private static DeviceReading Mouse(BatteryStatus? status) => new(new DeviceInfo(MouseId, "PRO X2"), status);
    private static DeviceReading Keyboard(BatteryStatus? status) => new(new DeviceInfo(KeyboardId, "G915"), status);

    private static TrayDevices Build(IReadOnlyList<DeviceReading> readings, DeviceId? saved = null, IReadOnlyDictionary<DeviceId, string>? names = null) =>
        TrayDevices.From(readings, saved, names ?? NoNames, Now);

    [Fact]
    public void The_menu_lists_every_device_with_its_battery_and_marks_the_tray_device()
    {
        var devices = Build([Mouse(TestData.Status(85)), Keyboard(TestData.Status(60))], saved: KeyboardId);

        Assert.Equal(
            [new TrayDeviceItem(MouseId, "PRO X2 · 85%", false), new TrayDeviceItem(KeyboardId, "G915 · 60%", true)],
            devices.Items);
    }

    [Fact]
    public void The_other_devices_are_all_devices_except_the_tray_device()
    {
        var keyboardStatus = TestData.Status(60);

        var devices = Build([Mouse(TestData.Status(85)), Keyboard(keyboardStatus)]);

        Assert.Equal("PRO X2", devices.TrayName);
        Assert.Equal([new OtherDevice("G915", keyboardStatus)], devices.Others);
    }

    [Fact]
    public void Menu_texts_say_asleep_and_not_found_in_lower_case()
    {
        var asleep = TestData.Status(percent: null, connection: ConnectionState.Asleep);

        var devices = Build([Mouse(TestData.Status(85)), Keyboard(asleep)]);

        Assert.Equal("G915 · asleep", devices.Items[1].Text);
    }

    [Fact]
    public void A_saved_device_that_was_not_read_is_listed_as_not_found_and_is_the_tray_device()
    {
        var names = new Dictionary<DeviceId, string> { [KeyboardId] = "G915" };

        var devices = Build([Mouse(TestData.Status(85))], saved: KeyboardId, names);

        Assert.Equal(new TrayDeviceItem(KeyboardId, "G915 · not found", true), devices.Items[1]);
        Assert.Equal("G915: Not found", $"{devices.TrayName}: {devices.StatusLine}");
    }

    [Fact]
    public void The_placeholder_for_a_missing_device_is_not_listed_but_is_shown_in_the_tray()
    {
        var placeholder = new DeviceReading(DeviceInfo.NoDeviceYet, TestData.Status(percent: null, connection: ConnectionState.NotFound));

        var devices = Build([placeholder]);

        Assert.Empty(devices.Items);
        Assert.Equal(DeviceInfo.NoDeviceYet.Name, devices.TrayName);
    }

    [Fact]
    public void A_failed_reading_of_the_tray_device_has_a_read_failed_status_line() =>
        Assert.Equal("Battery read failed", Build([Mouse(status: null)]).StatusLine);
}
