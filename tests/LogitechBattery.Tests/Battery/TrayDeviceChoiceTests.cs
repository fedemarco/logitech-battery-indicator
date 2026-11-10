using LogitechBattery.Battery;

namespace LogitechBattery.Tests;

public class TrayDeviceChoiceTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch.AddDays(1);
    private static readonly DeviceId MouseId = DeviceId.Parse("11111111");
    private static readonly DeviceId KeyboardId = DeviceId.Parse("22222222");
    private static readonly IReadOnlyDictionary<DeviceId, string> NoNames = new Dictionary<DeviceId, string>();

    private static DeviceReading Reading(DeviceId id, string name) => new(new DeviceInfo(id, name), TestData.Status());

    [Fact]
    public void The_saved_device_is_chosen_when_it_was_read()
    {
        var keyboard = Reading(KeyboardId, "G915");

        var chosen = TrayDeviceChoice.Pick([Reading(MouseId, "PRO X2"), keyboard], KeyboardId, NoNames, Now);

        Assert.Equal(keyboard, chosen);
    }

    [Fact]
    public void A_saved_device_that_was_not_read_shows_as_not_found_with_its_saved_name_instead_of_another_device()
    {
        var savedNames = new Dictionary<DeviceId, string> { [KeyboardId] = "G915" };

        var chosen = TrayDeviceChoice.Pick([Reading(MouseId, "PRO X2")], KeyboardId, savedNames, Now);

        Assert.Equal(new DeviceInfo(KeyboardId, "G915"), chosen!.Device);
        Assert.Equal(new BatteryStatus(ConnectionState.NotFound, null, ChargingState.Unknown, Now), chosen.Status);
    }

    [Fact]
    public void A_saved_device_without_a_saved_name_is_not_found_with_an_empty_name()
    {
        var chosen = TrayDeviceChoice.Pick([], KeyboardId, NoNames, Now);

        Assert.Equal(string.Empty, chosen!.Device.Name);
    }

    [Fact]
    public void Without_a_saved_device_the_first_device_is_chosen()
    {
        var mouse = Reading(MouseId, "PRO X2");

        Assert.Equal(mouse, TrayDeviceChoice.Pick([mouse, Reading(KeyboardId, "G915")], saved: null, NoNames, Now));
    }

    [Fact]
    public void Without_a_saved_device_and_without_readings_nothing_is_chosen() =>
        Assert.Null(TrayDeviceChoice.Pick([], saved: null, NoNames, Now));
}
