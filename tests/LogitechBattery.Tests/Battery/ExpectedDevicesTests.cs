using LogitechBattery.Battery;

namespace LogitechBattery.Tests;

public class ExpectedDevicesTests
{
    private static readonly DeviceId MouseId = DeviceId.FromRoute(productId: 0xC094, deviceIndex: 1);
    private static readonly DeviceId SecondMouseId = DeviceId.FromRoute(productId: 0xC094, deviceIndex: 2);

    private static DeviceReading Reading(DeviceId id, ConnectionState connection) =>
        new(new DeviceInfo(id, "PRO X2"), TestData.Status(connection == ConnectionState.Connected ? 80 : null, ChargingState.Unknown, connection));

    [Fact]
    public void All_remembered_devices_connected_is_true() =>
        Assert.True(ExpectedDevices.AllConnected(
            [MouseId, SecondMouseId],
            [Reading(MouseId, ConnectionState.Connected), Reading(SecondMouseId, ConnectionState.Connected)]));

    [Fact]
    public void A_remembered_device_missing_from_the_readings_is_false() =>
        Assert.False(ExpectedDevices.AllConnected(
            [MouseId, SecondMouseId],
            [Reading(MouseId, ConnectionState.Connected)]));

    [Fact]
    public void An_asleep_device_is_false() =>
        Assert.False(ExpectedDevices.AllConnected(
            [MouseId, SecondMouseId],
            [Reading(MouseId, ConnectionState.Connected), Reading(SecondMouseId, ConnectionState.Asleep)]));

    [Fact]
    public void A_new_device_in_the_readings_that_is_not_connected_is_false() =>
        Assert.False(ExpectedDevices.AllConnected(
            [MouseId],
            [Reading(MouseId, ConnectionState.Connected), Reading(SecondMouseId, ConnectionState.Asleep)]));

    [Fact]
    public void Connected_devices_without_remembered_names_are_true() =>
        Assert.True(ExpectedDevices.AllConnected([], [Reading(MouseId, ConnectionState.Connected)]));

    [Fact]
    public void Only_the_placeholder_and_nothing_remembered_is_false() =>
        Assert.False(ExpectedDevices.AllConnected([], [new DeviceReading(DeviceInfo.NoDeviceYet, null)]));

    [Fact]
    public void A_remembered_device_and_no_readings_is_false() =>
        Assert.False(ExpectedDevices.AllConnected([MouseId], []));

    [Fact]
    public void No_readings_and_nothing_remembered_is_false() =>
        Assert.False(ExpectedDevices.AllConnected([], []));
}
