namespace LogitechBattery.Tests;

public class HidPpDevicesTests
{
    [Fact]
    public void Collections_of_one_physical_device_share_a_key()
    {
        const string device = @"\\?\hid#vid_046d&pid_c54d&mi_02";

        Assert.Equal(HidPpDevices.GroupKey(device + "&col01#7&1a2b3c"), HidPpDevices.GroupKey(device + "&col02#7&1a2b3c"));
    }

    [Fact]
    public void The_key_drops_everything_from_the_collection_suffix()
    {
        Assert.Equal(@"\\?\hid#vid_046d&pid_c54d&mi_02", HidPpDevices.GroupKey(@"\\?\hid#vid_046d&pid_c54d&mi_02&COL01#7&1a2b3c"));
    }

    [Fact]
    public void A_path_without_a_collection_suffix_is_its_own_key()
    {
        const string path = @"\\?\hid#vid_046d&pid_c54d&mi_02#7&1a2b3c";

        Assert.Equal(path, HidPpDevices.GroupKey(path));
    }
}
