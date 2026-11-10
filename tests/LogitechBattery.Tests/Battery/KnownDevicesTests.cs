using LogitechBattery.Battery;

namespace LogitechBattery.Tests;

public class KnownDevicesTests
{
    private const string CableKey = "cable";
    private const string ReceiverKey = "receiver";

    private static readonly DeviceInfo Mouse = new(DeviceId.Parse("62C31634"), "Mouse");
    private static readonly DeviceInfo Keyboard = new(DeviceId.Parse("00000001"), "Keyboard");

    [Fact]
    public void Devices_are_listed_in_the_order_they_were_first_seen()
    {
        var known = new KnownDevices();
        known.Seen(Mouse, ReceiverKey);
        known.Seen(Keyboard, ReceiverKey);
        known.Seen(Mouse, CableKey);

        Assert.Equal([Mouse, Keyboard], known.InFirstSeenOrder);
    }

    [Fact]
    public void A_device_is_asleep_while_the_channel_it_was_last_seen_on_is_open()
    {
        var known = new KnownDevices();
        known.Seen(Mouse, ReceiverKey);

        Assert.Equal(ConnectionState.Asleep, known.StateWhenNotAnswering(Mouse, OpenChannels(ReceiverKey)));
    }

    [Fact]
    public void A_device_is_not_found_once_the_channel_it_was_last_seen_on_is_gone()
    {
        var known = new KnownDevices();
        known.Seen(Mouse, CableKey);

        Assert.Equal(ConnectionState.NotFound, known.StateWhenNotAnswering(Mouse, OpenChannels(ReceiverKey)));
    }

    [Fact]
    public void The_last_seen_channel_counts_when_a_device_moves_from_cable_to_receiver()
    {
        var known = new KnownDevices();
        known.Seen(Mouse, CableKey);
        known.Seen(Mouse, ReceiverKey);

        Assert.Equal(ConnectionState.Asleep, known.StateWhenNotAnswering(Mouse, OpenChannels(ReceiverKey)));
    }

    [Fact]
    public void Nothing_is_known_before_a_device_is_seen()
    {
        Assert.True(new KnownDevices().IsEmpty);
    }

    private static HashSet<string> OpenChannels(params string[] keys) => [.. keys];
}
