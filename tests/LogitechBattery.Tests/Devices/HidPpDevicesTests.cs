using LogitechBattery.Devices;

namespace LogitechBattery.Tests;

public class HidPpDevicesTests
{
    private const string Receiver1Col01 = @"\\?\hid#vid_046d&pid_c54d&mi_02&col01#a&13495dea&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}";
    private const string Receiver1Col02 = @"\\?\hid#vid_046d&pid_c54d&mi_02&col02#a&13495dea&0&0001#{4d1e55b2-f16f-11cf-88cb-001111000030}";
    private const string Receiver2Col01 = @"\\?\hid#vid_046d&pid_c54d&mi_02&col01#a&36e67c03&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}";

    [Fact]
    public void Long_and_short_collections_of_one_receiver_share_a_key()
    {
        Assert.Equal(HidPpDevices.GroupKey(Receiver1Col01), HidPpDevices.GroupKey(Receiver1Col02));
    }

    [Fact]
    public void Collections_of_one_receiver_share_a_key_when_the_col_case_differs()
    {
        var upperCaseCollection = Receiver1Col01.Replace("&col01", "&COL01");

        Assert.Equal(HidPpDevices.GroupKey(upperCaseCollection), HidPpDevices.GroupKey(Receiver1Col02));
    }

    [Fact]
    public void Two_receivers_of_the_same_model_get_different_keys()
    {
        Assert.NotEqual(HidPpDevices.GroupKey(Receiver1Col01), HidPpDevices.GroupKey(Receiver2Col01));
    }

    [Fact]
    public void A_path_without_the_Windows_shape_is_its_own_key()
    {
        const string path = "/dev/hidraw3";

        Assert.Equal(path, HidPpDevices.GroupKey(path));
    }

    [Fact]
    public void The_log_id_of_a_receiver_key_is_its_instance_part()
    {
        Assert.Equal("a&36e67c03&0", HidPpDevices.LogId(HidPpDevices.GroupKey(Receiver2Col01)));
    }

    [Fact]
    public void The_log_id_of_a_key_without_a_hash_is_the_whole_key()
    {
        Assert.Equal("/dev/hidraw3", HidPpDevices.LogId("/dev/hidraw3"));
    }
}
