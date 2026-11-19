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
    public void A_cable_found_after_the_receiver_route_replaces_it()
    {
        var receiver = new FakeHidPpChannel("receiver").With(FirstSlot, Mouse(MouseUnitId));
        var cable = new FakeHidPpChannel("cable").With(CableIndex, Mouse(MouseUnitId));
        var routes = new DeviceRoutes();
        routes.Discover([receiver], CancellationToken.None);

        routes.Discover([receiver, cable], CancellationToken.None);

        var route = Assert.Single(routes.All);
        Assert.Same(cable, route.Channel);
        Assert.Equal(CableIndex, route.DeviceIndex);
    }

    [Fact]
    public void A_routed_device_on_an_open_channel_keeps_its_route_and_its_slot_gets_no_new_requests_at_the_next_discovery()
    {
        var receiver = new FakeHidPpChannel().With(FirstSlot, Mouse(MouseUnitId));
        var routes = new DeviceRoutes();
        routes.Discover([receiver], CancellationToken.None);
        var requestsAfterFirstDiscovery = receiver.RequestsTo(FirstSlot);

        routes.Discover([receiver], CancellationToken.None);

        Assert.Equal(FirstSlot, Assert.Single(routes.All).DeviceIndex);
        Assert.Equal(requestsAfterFirstDiscovery, receiver.RequestsTo(FirstSlot));
    }

    [Fact]
    public void The_name_of_a_device_found_is_known_by_its_slot()
    {
        var routes = DiscoverMouseOnFirstSlot(out var channel);

        Assert.Equal("PRO X2 SUPERSTRIKE", routes.NameAt(new Slot(channel.Key, FirstSlot)));
    }

    [Fact]
    public void The_name_stays_known_after_the_route_is_dropped()
    {
        var routes = DiscoverMouseOnFirstSlot(out var channel);

        routes.Drop(Assert.Single(routes.All));

        Assert.Equal("PRO X2 SUPERSTRIKE", routes.NameAt(new Slot(channel.Key, FirstSlot)));
    }

    [Fact]
    public void The_name_stays_known_after_the_routes_are_cleared()
    {
        var routes = DiscoverMouseOnFirstSlot(out var channel);

        routes.Clear();

        Assert.Equal("PRO X2 SUPERSTRIKE", routes.NameAt(new Slot(channel.Key, FirstSlot)));
    }

    [Fact]
    public void A_slot_where_no_device_was_found_has_no_name()
    {
        var routes = DiscoverMouseOnFirstSlot(out var channel);

        Assert.Null(routes.NameAt(new Slot(channel.Key, SecondSlot)));
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
        AssertProbedAgainOnlyAfterForgettingSkippedSlots(channel, routes);
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
        AssertProbedAgainOnlyAfterForgettingSkippedSlots(channel, routes);
    }

    [Fact]
    public void A_channel_that_throws_an_IOException_is_returned_as_failed()
    {
        var channel = new FakeHidPpChannel { Failure = new IOException() };

        var failedChannels = new DeviceRoutes().Discover([channel], CancellationToken.None);

        Assert.Same(channel, Assert.Single(failedChannels));
    }

    private static void AssertProbedAgainOnlyAfterForgettingSkippedSlots(FakeHidPpChannel channel, DeviceRoutes routes)
    {
        var requestsAfterFirstDiscovery = channel.RequestsTo(FirstSlot);

        routes.Discover([channel], CancellationToken.None);
        Assert.Equal(requestsAfterFirstDiscovery, channel.RequestsTo(FirstSlot));

        routes.ForgetSkippedSlots();
        routes.Discover([channel], CancellationToken.None);
        Assert.True(channel.RequestsTo(FirstSlot) > requestsAfterFirstDiscovery);
    }

    private static DeviceRoutes DiscoverMouseOnFirstSlot(out FakeHidPpChannel channel)
    {
        channel = new FakeHidPpChannel().With(FirstSlot, Mouse(MouseUnitId));
        var routes = new DeviceRoutes();
        routes.Discover([channel], CancellationToken.None);
        return routes;
    }

    private static FakeDevice Mouse(string unitId) =>
        FakeDevice.Awake().WithUnifiedBattery().WithName("PRO X2 SUPERSTRIKE").WithUnitId(unitId);
}
