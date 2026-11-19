namespace LogitechBattery.Tests;

public class HidPpDevicesTests
{
    [Fact]
    public void Collections_of_one_physical_device_share_a_key_without_the_collection_suffix()
    {
        const string device = @"\\?\hid#vid_046d&pid_c54d&mi_02";

        Assert.Equal(device, HidPpDevices.GroupKey(device + "&COL01#7&1a2b3c"));
        Assert.Equal(device, HidPpDevices.GroupKey(device + "&col02#7&1a2b3c"));
    }

    [Fact]
    public void A_path_without_a_collection_suffix_is_its_own_key()
    {
        const string path = @"\\?\hid#vid_046d&pid_c54d&mi_02#7&1a2b3c";

        Assert.Equal(path, HidPpDevices.GroupKey(path));
    }
}
