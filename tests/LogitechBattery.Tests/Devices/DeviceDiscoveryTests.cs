using LogitechBattery.Battery;
using LogitechBattery.Devices;
using LogitechBattery.HidPp;

namespace LogitechBattery.Tests;

public class DeviceDiscoveryTests
{
    private const byte DeviceIndex = 1;
    private const int ProductId = 0xC54D;
    private const string UnitId = "1A2B3C4D";
    private const string MouseName = "PRO X2 SUPERSTRIKE";
    private const byte PermanentErrorCode = 0x05;
    private static readonly DeviceId RouteId = DeviceId.FromRoute(ProductId, DeviceIndex);

    [Fact]
    public void A_device_with_Unified_Battery_is_found_with_its_name_and_unit_id()
    {
        var device = FakeDevice.Awake().WithUnifiedBattery().WithName(MouseName).WithUnitId(UnitId);

        var target = Found(Probe(device));

        Assert.Equal(MouseName, target.Device.Name);
        Assert.Equal(UnitId, target.Device.Id.ToString());
    }

    [Fact]
    public void A_silent_slot_is_no_device()
    {
        Assert.Same(SlotProbe.NoDevice, Probe(device: null));
    }

    [Fact]
    public void A_device_without_Unified_Battery_is_skipped_with_its_name()
    {
        var device = FakeDevice.Awake().WithName("Keyboard");

        var probe = Assert.IsType<SlotProbe.NoUnifiedBattery>(Probe(device));

        Assert.Equal("Keyboard", probe.DeviceName);
    }

    [Fact]
    public void An_unanswered_Unified_Battery_lookup_is_no_device_not_a_missing_feature()
    {
        var device = FakeDevice.Awake().WithoutFeatureLookupAnswers();

        Assert.Same(SlotProbe.NoDevice, Probe(device));
    }

    [Fact]
    public void A_busy_device_info_is_no_device_so_it_is_read_again_next_time()
    {
        var device = BatteryDevice().WithDeviceInfoError(DeviceInformation.BusyErrorCode);

        Assert.Same(SlotProbe.NoDevice, Probe(device));
    }

    [Fact]
    public void Another_device_info_error_gives_the_route_id()
    {
        var device = BatteryDevice().WithDeviceInfoError(PermanentErrorCode);

        Assert.Equal(RouteId, Found(Probe(device)).Device.Id);
    }

    [Fact]
    public void A_device_without_the_device_information_feature_gets_the_route_id()
    {
        Assert.Equal(RouteId, Found(Probe(BatteryDevice())).Device.Id);
    }

    [Fact]
    public void A_too_short_device_info_answer_gives_the_route_id()
    {
        var device = BatteryDevice().WithTooShortDeviceInfo();

        Assert.Equal(RouteId, Found(Probe(device)).Device.Id);
    }

    [Fact]
    public void An_invalid_unit_id_gives_the_route_id()
    {
        var device = BatteryDevice().WithUnitId("00000000");

        Assert.Equal(RouteId, Found(Probe(device)).Device.Id);
    }

    private static FakeDevice BatteryDevice() => FakeDevice.Awake().WithUnifiedBattery().WithName(MouseName);

    private static SlotProbe Probe(FakeDevice? device)
    {
        var channel = new FakeHidPpChannel(productId: ProductId);
        if (device is not null)
            channel.With(DeviceIndex, device);
        return DeviceDiscovery.Probe(channel, DeviceIndex, CancellationToken.None);
    }

    private static BatteryTarget Found(SlotProbe probe) => Assert.IsType<SlotProbe.Found>(probe).Target;
}
