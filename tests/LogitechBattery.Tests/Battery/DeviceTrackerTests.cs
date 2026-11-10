using LogitechBattery.Battery;

namespace LogitechBattery.Tests;

public class DeviceTrackerTests
{
    private static readonly DeviceInfo Mouse = new(DeviceId.Parse("11111111"), "PRO X2");
    private static readonly DeviceInfo Keyboard = new(DeviceId.Parse("22222222"), "G915");
    private static readonly IReadOnlyDictionary<DeviceId, string> NoNames = new Dictionary<DeviceId, string>();
    private static readonly AlertSettings Settings = AlertSettings.Default;

    private static DeviceReading Discharging(DeviceInfo device, int percent) => new(device, TestData.Status(percent));

    [Fact]
    public void Every_device_alerts_with_its_own_name()
    {
        var tracker = new DeviceTracker(NoNames);

        var update = tracker.Update([Discharging(Mouse, 10), Discharging(Keyboard, 4)], Settings);

        Assert.Equal(
            [new DeviceAlert("PRO X2", BatteryAlert.Low, 10), new DeviceAlert("G915", BatteryAlert.Critical, 4)],
            update.Alerts);
    }

    [Fact]
    public void A_device_alerts_once_while_another_device_alerts_for_its_own_cycle()
    {
        var tracker = new DeviceTracker(NoNames);
        tracker.Update([Discharging(Mouse, 10)], Settings);

        var update = tracker.Update([Discharging(Mouse, 9), Discharging(Keyboard, 10)], Settings);

        Assert.Equal([new DeviceAlert("G915", BatteryAlert.Low, 10)], update.Alerts);
    }

    [Fact]
    public void Devices_with_the_same_name_are_told_apart_in_the_alert()
    {
        var secondMouse = new DeviceInfo(DeviceId.Parse("3333EF01"), "PRO X2");
        var tracker = new DeviceTracker(NoNames);

        var update = tracker.Update([Discharging(Mouse, 10), Discharging(secondMouse, 10)], Settings);

        Assert.Equal(["PRO X2 (1111)", "PRO X2 (EF01)"], update.Alerts.Select(alert => alert.DeviceName));
    }

    [Fact]
    public void A_failed_reading_gives_no_alert()
    {
        var update = new DeviceTracker(NoNames).Update([new DeviceReading(Mouse, Status: null)], Settings);

        Assert.Empty(update.Alerts);
    }

    [Fact]
    public void The_not_yet_found_placeholder_gives_no_alert_and_no_name_to_save()
    {
        var update = new DeviceTracker(NoNames).Update([Discharging(DeviceInfo.NoDeviceYet, 3)], Settings);

        Assert.Empty(update.Alerts);
        Assert.Null(update.NamesToSave);
    }

    [Fact]
    public void A_new_device_is_reported_for_saving_together_with_the_names_already_saved()
    {
        var tracker = new DeviceTracker(new Dictionary<DeviceId, string> { [Keyboard.Id] = Keyboard.Name });

        var update = tracker.Update([Discharging(Mouse, 50)], Settings);

        Assert.Equal(
            new Dictionary<DeviceId, string> { [Keyboard.Id] = "G915", [Mouse.Id] = "PRO X2" },
            update.NamesToSave);
    }

    [Fact]
    public void Names_are_reported_for_saving_only_once_until_a_name_changes()
    {
        var tracker = new DeviceTracker(NoNames);
        tracker.Update([Discharging(Mouse, 50)], Settings);

        Assert.Null(tracker.Update([Discharging(Mouse, 50)], Settings).NamesToSave);

        var renamed = Mouse with { Name = "PRO X2 SUPERSTRIKE" };
        var update = tracker.Update([Discharging(renamed, 50)], Settings);
        Assert.Equal("PRO X2 SUPERSTRIKE", update.NamesToSave![Mouse.Id]);
    }

    [Fact]
    public void A_device_already_saved_under_the_same_name_is_not_reported()
    {
        var tracker = new DeviceTracker(new Dictionary<DeviceId, string> { [Mouse.Id] = Mouse.Name });

        Assert.Null(tracker.Update([Discharging(Mouse, 50)], Settings).NamesToSave);
    }

    [Fact]
    public void A_name_that_the_settings_file_cleans_is_not_reported_again_after_it_was_saved_cleaned()
    {
        var messyMouse = Mouse with { Name = "  PRO\tX2\n" };
        var savedCleaned = new Dictionary<DeviceId, string> { [Mouse.Id] = "PROX2" };

        var update = new DeviceTracker(savedCleaned).Update([Discharging(messyMouse, 50)], Settings);

        Assert.Null(update.NamesToSave);
    }
}
