using LogitechBattery.HidPp;

namespace LogitechBattery.Tests;

public class DeviceNameTests
{
    [Fact]
    public void Assembles_ProbeSample()
    {
        var first = new byte[] { 0x50, 0x52, 0x4f, 0x20, 0x58, 0x32, 0x20, 0x53, 0x55, 0x50, 0x45, 0x52, 0x53, 0x54, 0x52, 0x49 };
        var second = new byte[16];
        second[0] = 0x4b;
        second[1] = 0x45;
        Assert.Equal("PRO X2 SUPERSTRIKE", DeviceName.Assemble(0x12, [first, second]));
    }

    [Fact]
    public void Truncates_ToLength() =>
        Assert.Equal("AB", DeviceName.Assemble(2, [[(byte)'A', (byte)'B', (byte)'C']]));
}
