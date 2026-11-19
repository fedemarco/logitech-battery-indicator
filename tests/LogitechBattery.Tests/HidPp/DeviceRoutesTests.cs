using LogitechBattery.HidPp;

namespace LogitechBattery.Tests;

public class DeviceRoutesTests
{
    private const byte CableIndex = 0xFF;
    private const byte FirstSlot = 1;
    private const byte SecondSlot = 2;
    private const string MouseUnitId = "1A2B3C4D";
    private const string OtherUnitId = "5E6F7A8B";

    [Fact]
    public void The_same_mouse_on_the_cable_and_on_a_receiver_gets_one_route_the_cables()
    {
        var receiver = new FakeHidPpChannel("receiver").With(FirstSlot, Mouse(MouseUnitId));
        var cable = new FakeHidPpChannel("cable").With(CableIndex, Mouse(MouseUnitId));
        var routes = new DeviceRoutes();

        routes.Discover([receiver, cable], CancellationToken.None);

        var route = Assert.Single(routes.All);
        Assert.Same(cable, route.Channel);
        Assert.Equal(CableIndex, route.DeviceIndex);
    }

    [Fact]
    public void Receiver_slots_are_not_probed_on_a_channel_where_the_cable_index_answers()
    {
        var cable = new FakeHidPpChannel().With(CableIndex, Mouse(MouseUnitId));

        new DeviceRoutes().Discover([cable], CancellationToken.None);

        Assert.All(HidPpMessage.ReceiverDeviceIndexes, slot => Assert.Equal(0, cable.RequestsTo(slot)));
    }

    [Fact]
    public void A_skipped_slot_is_not_probed_at_the_next_discovery_but_is_after_forgetting_skipped_slots()
    {
        var channel = new FakeHidPpChannel().With(FirstSlot, FakeDevice.Awake().WithName("Keyboard"));
        var routes = new DeviceRoutes();
        routes.Discover([channel], CancellationToken.None);
        var requestsAfterFirstDiscovery = channel.RequestsTo(FirstSlot);

        routes.Discover([channel], CancellationToken.None);
        Assert.Equal(requestsAfterFirstDiscovery, channel.RequestsTo(FirstSlot));

        routes.ForgetSkippedSlots();
        routes.Discover([channel], CancellationToken.None);
        Assert.True(channel.RequestsTo(FirstSlot) > requestsAfterFirstDiscovery);
    }

    [Fact]
    public void An_unexpected_exception_in_one_slot_does_not_stop_the_other_slots()
    {
        var channel = new FakeHidPpChannel()
            .With(FirstSlot, FakeDevice.Awake().ThrowingOnEveryRequest(new InvalidOperationException()))
            .With(SecondSlot, Mouse(OtherUnitId));
        var routes = new DeviceRoutes();

        var failedChannels = routes.Discover([channel], CancellationToken.None);

        Assert.Empty(failedChannels);
        Assert.Equal(SecondSlot, Assert.Single(routes.All).DeviceIndex);
    }

    [Fact]
    public void A_slot_that_threw_is_not_probed_again_until_skipped_slots_are_forgotten()
    {
        var channel = new FakeHidPpChannel().With(FirstSlot, FakeDevice.Awake().ThrowingOnEveryRequest(new InvalidOperationException()));
        var routes = new DeviceRoutes();
        routes.Discover([channel], CancellationToken.None);
        var requestsAfterFirstDiscovery = channel.RequestsTo(FirstSlot);

        routes.Discover([channel], CancellationToken.None);
        Assert.Equal(requestsAfterFirstDiscovery, channel.RequestsTo(FirstSlot));

        routes.ForgetSkippedSlots();
        routes.Discover([channel], CancellationToken.None);
        Assert.True(channel.RequestsTo(FirstSlot) > requestsAfterFirstDiscovery);
    }

    [Fact]
    public void A_channel_that_throws_an_IOException_is_returned_as_failed()
    {
        var channel = new FakeHidPpChannel { Failure = new IOException() };

        var failedChannels = new DeviceRoutes().Discover([channel], CancellationToken.None);

        Assert.Same(channel, Assert.Single(failedChannels));
    }

    private static FakeDevice Mouse(string unitId) =>
        FakeDevice.Awake().WithUnifiedBattery().WithName("PRO X2 SUPERSTRIKE").WithUnitId(unitId);
}
