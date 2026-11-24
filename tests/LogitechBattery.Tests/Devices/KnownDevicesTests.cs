using LogitechBattery.Battery;
using LogitechBattery.Devices;

namespace LogitechBattery.Tests;

public class KnownDevicesTests
{
    private const string CableKey = "cable";
    private const string ReceiverKey = "receiver";
    private const string OtherReceiverKey = "other-receiver";
    private const byte FirstSlot = 1;
    private const byte SecondSlot = 2;

    private static readonly DeviceInfo Mouse = new(DeviceId.Parse("62C31634"), "Mouse");
    private static readonly DeviceInfo Keyboard = new(DeviceId.Parse("00000001"), "Keyboard");

    [Fact]
    public void Devices_are_listed_in_the_order_they_were_first_seen()
    {
        var known = new KnownDevices();
        known.Seen(Mouse, On(ReceiverKey));
        known.Seen(Keyboard, On(ReceiverKey));
        known.Seen(Mouse, On(CableKey));

        Assert.Equal([Mouse, Keyboard], known.InFirstSeenOrder);
    }

    [Fact]
    public void A_device_is_asleep_while_the_channel_it_was_last_seen_on_is_open()
    {
        var known = new KnownDevices();
        known.Seen(Mouse, On(ReceiverKey));

        Assert.Equal(ConnectionState.Asleep, known.StateWhenNotAnswering(Mouse, OpenChannels(ReceiverKey)));
    }

    [Fact]
    public void A_device_is_not_found_once_the_channel_it_was_last_seen_on_is_gone()
    {
        var known = new KnownDevices();
        known.Seen(Mouse, On(CableKey));

        Assert.Equal(ConnectionState.NotFound, known.StateWhenNotAnswering(Mouse, OpenChannels(ReceiverKey)));
    }

    [Fact]
    public void The_last_seen_channel_counts_when_a_device_moves_from_cable_to_receiver()
    {
        var known = new KnownDevices();
        known.Seen(Mouse, On(CableKey));
        known.Seen(Mouse, On(ReceiverKey));

        Assert.Equal(ConnectionState.Asleep, known.StateWhenNotAnswering(Mouse, OpenChannels(ReceiverKey)));
    }

    [Fact]
    public void A_later_real_name_replaces_the_stored_one()
    {
        var known = new KnownDevices();
        known.Seen(Mouse, On(ReceiverKey));
        var renamed = Mouse with { Name = "Mouse 2" };
        known.Seen(renamed, On(ReceiverKey));

        Assert.Equal([renamed], known.InFirstSeenOrder);
    }

    [Fact]
    public void Seeing_a_device_again_on_the_same_slot_is_no_news()
    {
        var known = new KnownDevices();

        Assert.True(known.Seen(Mouse, On(ReceiverKey)));
        Assert.False(known.Seen(Mouse, On(ReceiverKey)));
    }

    [Fact]
    public void Seeing_a_device_on_another_slot_is_news()
    {
        var known = new KnownDevices();
        known.Seen(Mouse, On(ReceiverKey, FirstSlot));

        Assert.True(known.Seen(Mouse, On(ReceiverKey, SecondSlot)));
    }

    [Fact]
    public void An_open_channel_without_a_known_device_is_found_while_another_channel_has_one()
    {
        var known = new KnownDevices();
        known.Seen(Mouse, On(ReceiverKey));

        Assert.True(known.AnyChannelWithoutDevice(OpenChannels(ReceiverKey, OtherReceiverKey)));
    }

    [Fact]
    public void A_channel_without_a_known_device_is_ignored_while_it_is_not_open()
    {
        var known = new KnownDevices();
        known.Seen(Mouse, On(ReceiverKey));

        Assert.False(known.AnyChannelWithoutDevice(OpenChannels(ReceiverKey)));
    }

    [Fact]
    public void No_channel_is_without_a_device_when_a_known_device_was_last_seen_on_each_open_channel()
    {
        var known = new KnownDevices();
        known.Seen(Mouse, On(ReceiverKey));
        known.Seen(Keyboard, On(OtherReceiverKey));

        Assert.False(known.AnyChannelWithoutDevice(OpenChannels(ReceiverKey, OtherReceiverKey)));
    }

    [Fact]
    public void No_channel_is_without_a_device_when_no_channel_is_open()
    {
        var known = new KnownDevices();

        Assert.False(known.AnyChannelWithoutDevice(OpenChannels()));
    }

    [Fact]
    public void A_device_that_moved_to_another_channel_no_longer_counts_for_the_first_one()
    {
        var known = new KnownDevices();
        known.Seen(Mouse, On(CableKey));
        known.Seen(Mouse, On(ReceiverKey));

        Assert.True(known.AnyChannelWithoutDevice(OpenChannels(CableKey)));
    }

    private static Slot On(string channelKey, byte deviceIndex = FirstSlot) => new(channelKey, deviceIndex);

    private static HashSet<string> OpenChannels(params string[] keys) => [.. keys];
}
