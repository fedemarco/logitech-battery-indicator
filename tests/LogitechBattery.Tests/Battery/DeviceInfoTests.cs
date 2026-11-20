using LogitechBattery.Battery;

namespace LogitechBattery.Tests;

public class DeviceInfoTests
{
    [Fact]
    public void The_no_device_yet_stand_in_is_a_placeholder()
    {
        Assert.True(DeviceInfo.NoDeviceYet.IsPlaceholder);
    }

    [Fact]
    public void A_real_device_is_not_a_placeholder()
    {
        var mouse = new DeviceInfo(DeviceId.FromRoute(productId: 0xC54D, deviceIndex: 1), DeviceInfo.ProX2SuperstrikeName);

        Assert.False(mouse.IsPlaceholder);
    }
}
