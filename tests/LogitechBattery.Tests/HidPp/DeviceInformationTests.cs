using LogitechBattery.HidPp;

namespace LogitechBattery.Tests;

public class DeviceInformationTests
{
    [Fact]
    public void Unit_id_is_the_four_bytes_after_the_entity_count_as_uppercase_hex()
    {
        byte[] parameters = [0x01, 0x1A, 0x2B, 0x3C, 0x4D, 0x00, 0x05, 0xFF];

        Assert.Equal("1A2B3C4D", DeviceInformation.UnitIdOf(parameters));
    }
}
