using LogitechBattery.Battery;
using LogitechBattery.Tray;

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

    [Theory]
    [InlineData(ConnectionState.Asleep)]
    [InlineData(ConnectionState.NotFound)]
    public void While_no_device_is_known_the_saved_device_takes_the_receiver_state_with_its_saved_name(ConnectionState receiverState)
    {
        var placeholder = new DeviceReading(DeviceInfo.NoDeviceYet, TestData.Status(percent: null, connection: receiverState));
        var names = new Dictionary<DeviceId, string> { [MouseId] = "PRO X2" };

        var chosen = TrayDeviceChoice.Pick([placeholder], MouseId, names, Now);

        Assert.Equal("PRO X2", chosen!.Device.Name);
        Assert.Equal(receiverState, chosen.Status!.Connection);
    }

    [Fact]
    public void A_saved_device_that_was_not_read_is_asleep_when_the_placeholder_says_a_receiver_has_no_known_device()
    {
        var savedNames = new Dictionary<DeviceId, string> { [KeyboardId] = "G915" };

        var chosen = TrayDeviceChoice.Pick([Reading(MouseId, "PRO X2"), TestData.AsleepPlaceholder()], KeyboardId, savedNames, Now);

        Assert.Equal("G915", chosen!.Device.Name);
        Assert.Equal(ConnectionState.Asleep, chosen.Status!.Connection);
    }

    [Fact]
    public void Without_a_saved_device_the_first_real_device_is_chosen_before_a_trailing_placeholder()
    {
        var mouse = Reading(MouseId, "PRO X2");

        Assert.Equal(mouse, TrayDeviceChoice.Pick([mouse, TestData.AsleepPlaceholder()], saved: null, NoNames, Now));
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

    [Fact]
    public void Without_a_saved_device_the_first_real_device_is_saved()
    {
        var placeholder = new DeviceReading(DeviceInfo.NoDeviceYet, null);

        var toSave = TrayDeviceChoice.FirstPickToSave([placeholder, Reading(MouseId, "PRO X2"), Reading(KeyboardId, "G915")], saved: null);

        Assert.Equal(MouseId, toSave);
    }

    [Fact]
    public void Nothing_is_saved_when_a_device_is_already_saved() =>
        Assert.Null(TrayDeviceChoice.FirstPickToSave([Reading(MouseId, "PRO X2")], saved: KeyboardId));

    [Fact]
    public void Nothing_is_saved_when_the_only_reading_is_the_placeholder() =>
        Assert.Null(TrayDeviceChoice.FirstPickToSave([new DeviceReading(DeviceInfo.NoDeviceYet, null)], saved: null));

    [Fact]
    public void Nothing_is_saved_without_readings() =>
        Assert.Null(TrayDeviceChoice.FirstPickToSave([], saved: null));
}
