using LogitechBattery.HidPp;

namespace LogitechBattery.Tests;

public class HidPpChannelTests
{
    private const byte FirstId = 0x1;
    private const byte LastId = 0xE;
    private const byte DeviceIndex = 1;
    private const byte FeatureIndex = 0x08;
    private const byte FunctionAndSoftwareId = 0x1A;
    private const byte Hidpp10ErrorMarker = 0x8F;

    [Fact]
    public void The_software_id_counts_up_and_wraps_from_E_to_1()
    {
        Assert.Equal(0x2, HidPpChannel.NextSoftwareId(FirstId));
        Assert.Equal(FirstId, HidPpChannel.NextSoftwareId(LastId));
    }

    [Theory]
    [MemberData(nameof(EverySoftwareId))]
    public void The_software_id_is_never_0_or_F(byte currentSoftwareId)
    {
        Assert.InRange(HidPpChannel.NextSoftwareId(currentSoftwareId), FirstId, LastId);
    }

    public static IEnumerable<object[]> EverySoftwareId() =>
        Enumerable.Range(FirstId, LastId - FirstId + 1).Select(softwareId => new object[] { (byte)softwareId });

    [Fact]
    public void A_HID_plus_plus_1_error_is_no_answer()
    {
        byte[] request = [HidPpMessage.ShortId, DeviceIndex, FeatureIndex, FunctionAndSoftwareId, 0, 0, 0];
        byte[] error = [HidPpMessage.ShortId, DeviceIndex, Hidpp10ErrorMarker, FeatureIndex, FunctionAndSoftwareId, 0x05, 0];

        Assert.Equal(HidPpAnswer.Nothing, HidPpChannel.ToAnswer(request, error));
    }
}
