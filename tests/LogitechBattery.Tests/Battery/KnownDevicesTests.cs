using LogitechBattery.Battery;

namespace LogitechBattery.Tests;

public class KnownDevicesTests
{
    private const string CableKey = "cable";
    private const string ReceiverKey = "receiver";
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
    public void Nothing_is_known_before_a_device_is_seen()
    {
        Assert.True(new KnownDevices().IsEmpty);
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

    private static Slot On(string channelKey, byte deviceIndex = FirstSlot) => new(channelKey, deviceIndex);

    private static HashSet<string> OpenChannels(params string[] keys) => [.. keys];
}
