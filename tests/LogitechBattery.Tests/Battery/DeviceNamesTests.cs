using LogitechBattery.Battery;

namespace LogitechBattery.Tests;

public class DeviceNamesTests
{
    private static readonly DeviceId MouseId = DeviceId.Parse("11111111");
    private static readonly DeviceId KeyboardId = DeviceId.Parse("2222ABCD");
    private static readonly DeviceId SecondMouseId = DeviceId.Parse("3333EF01");

    private static string DisplayNameOf(DeviceId id, params DeviceInfo[] devices) => DeviceNames.Display(devices)[id];

    [Fact]
    public void A_unique_name_is_shown_as_it_is() =>
        Assert.Equal("PRO X2", DisplayNameOf(MouseId, new DeviceInfo(MouseId, "PRO X2"), new DeviceInfo(KeyboardId, "G915")));

    [Fact]
    public void Control_characters_are_removed() =>
        Assert.Equal("PRO X2", DisplayNameOf(MouseId, new DeviceInfo(MouseId, "PRO\0 X2\n")));

    [Fact]
    public void A_long_name_is_cut_to_the_maximum_length() =>
        Assert.Equal(new string('n', DeviceNames.MaxLength), DisplayNameOf(MouseId, new DeviceInfo(MouseId, new string('n', 50))));

    [Fact]
    public void Names_that_occur_twice_get_the_last_four_id_characters()
    {
        var names = DeviceNames.Display([new DeviceInfo(MouseId, "PRO X2"), new DeviceInfo(SecondMouseId, "PRO X2"), new DeviceInfo(KeyboardId, "G915")]);

        Assert.Equal("PRO X2 (1111)", names[MouseId]);
        Assert.Equal("PRO X2 (EF01)", names[SecondMouseId]);
        Assert.Equal("G915", names[KeyboardId]);
    }

    [Fact]
    public void Names_that_are_equal_after_cleaning_count_as_the_same_name()
    {
        var names = DeviceNames.Display([new DeviceInfo(MouseId, "PRO X2"), new DeviceInfo(SecondMouseId, "PRO\0 X2")]);

        Assert.Equal("PRO X2 (1111)", names[MouseId]);
        Assert.Equal("PRO X2 (EF01)", names[SecondMouseId]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("\0\n")]
    public void An_empty_name_becomes_logitech_device_with_the_id_characters(string name) =>
        Assert.Equal("Logitech device (ABCD)", DisplayNameOf(KeyboardId, new DeviceInfo(KeyboardId, name)));
}
