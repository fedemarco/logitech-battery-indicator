using LogitechBattery.HidPp;

namespace LogitechBattery.Tests;

public class ReceiverNotificationsTests
{
    [Fact]
    public void Connection_notification_with_the_not_established_bit_means_the_link_was_lost()
    {
        var isConnection = ReceiverNotifications.TryReadConnection([0x10, 0x01, 0x41, 0x11, 0x62, 0xBD, 0x40], out var deviceIndex, out var linkEstablished);

        Assert.True(isConnection);
        Assert.Equal(1, deviceIndex);
        Assert.False(linkEstablished);
    }

    [Fact]
    public void Connection_notification_without_the_not_established_bit_means_the_link_is_up()
    {
        var isConnection = ReceiverNotifications.TryReadConnection([0x10, 0x01, 0x41, 0x11, 0xA2, 0xBD, 0x40], out var deviceIndex, out var linkEstablished);

        Assert.True(isConnection);
        Assert.Equal(1, deviceIndex);
        Assert.True(linkEstablished);
    }

    [Fact]
    public void A_HID_pp_1_0_error_is_not_a_connection_notification() =>
        Assert.False(ReceiverNotifications.TryReadConnection([0x10, 0x01, 0x8F, 0x06, 0x1A, 0x08, 0x00], out _, out _));

    [Fact]
    public void Enabling_wireless_sets_only_that_bit()
    {
        Assert.True(ReceiverNotifications.TryEnableWireless([0x00, 0x00, 0x00], out var updated));
        Assert.Equal([0x00, 0x01, 0x00], updated);
    }

    [Fact]
    public void Enabling_wireless_keeps_the_other_flags()
    {
        Assert.True(ReceiverNotifications.TryEnableWireless([0x08, 0x00, 0x02], out var updated));
        Assert.Equal([0x08, 0x01, 0x02], updated);
    }

    [Fact]
    public void Nothing_is_written_when_wireless_is_already_on() =>
        Assert.False(ReceiverNotifications.TryEnableWireless([0x00, 0x01, 0x00], out _));

    [Fact]
    public void Nothing_is_written_when_the_receiver_answers_with_fewer_than_three_flag_bytes() =>
        Assert.False(ReceiverNotifications.TryEnableWireless([0x00, 0x00], out _));
}
