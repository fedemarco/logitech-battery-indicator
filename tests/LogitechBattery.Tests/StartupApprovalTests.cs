namespace LogitechBattery.Tests;

public class StartupApprovalTests
{
    private static byte[] ToggleValue(byte firstByte) => [firstByte, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];

    [Fact]
    public void No_value_means_the_entry_is_approved() =>
        Assert.True(StartupApproval.IsApproved(null));

    [Fact]
    public void An_empty_value_means_the_entry_is_approved() =>
        Assert.True(StartupApproval.IsApproved(Array.Empty<byte>()));

    [Fact]
    public void A_value_that_is_not_binary_means_the_entry_is_approved() =>
        Assert.True(StartupApproval.IsApproved("0x03"));

    [Fact]
    public void Task_Manager_enabled_is_an_even_first_byte() =>
        Assert.True(StartupApproval.IsApproved(ToggleValue(0x02)));

    [Fact]
    public void Task_Manager_disabled_is_an_odd_first_byte() =>
        Assert.False(StartupApproval.IsApproved(ToggleValue(0x03)));
}
