using LogitechBattery.Battery;
using LogitechBattery.Devices;

namespace LogitechBattery.Tests;

public class DeviceReadingsBuilderTests
{
    private const string ReceiverKey = "receiver";
    private const string OtherReceiverKey = "other-receiver";
    private const byte FirstSlot = 1;
    private const int MousePercent = 80;

    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;
    private static readonly DeviceInfo Mouse = new(DeviceId.Parse("62C31634"), "Mouse");
    private static readonly DeviceInfo Keyboard = new(DeviceId.Parse("00000001"), "Keyboard");

    [Fact]
    public void Without_a_known_device_an_open_channel_gives_one_asleep_placeholder()
    {
        var readings = Build(new KnownDevices(), NoAnswers(), OpenChannels(ReceiverKey));

        Assert.Equal([ReadingWithState(DeviceInfo.NoDeviceYet, ConnectionState.Asleep)], readings);
    }

    [Fact]
    public void Without_a_known_device_and_no_open_channel_the_placeholder_is_not_found()
    {
        var readings = Build(new KnownDevices(), NoAnswers(), OpenChannels());

        Assert.Equal([ReadingWithState(DeviceInfo.NoDeviceYet, ConnectionState.NotFound)], readings);
    }

    [Fact]
    public void A_known_device_that_answered_keeps_its_status()
    {
        var known = KnownOn(ReceiverKey, Mouse);
        var answer = TestData.Status(percent: MousePercent, at: Now);

        var readings = Build(known, AnswersOf((Mouse, answer)), OpenChannels(ReceiverKey));

        Assert.Equal([new DeviceReading(Mouse, answer)], readings);
    }

    [Fact]
    public void A_known_device_that_did_not_answer_is_asleep_while_its_channel_is_open()
    {
        var known = KnownOn(ReceiverKey, Mouse);

        var readings = Build(known, NoAnswers(), OpenChannels(ReceiverKey));

        Assert.Equal([ReadingWithState(Mouse, ConnectionState.Asleep)], readings);
    }

    [Fact]
    public void A_known_device_that_did_not_answer_is_not_found_once_its_channel_is_gone()
    {
        var known = KnownOn(ReceiverKey, Mouse);

        var readings = Build(known, NoAnswers(), OpenChannels());

        Assert.Equal([ReadingWithState(Mouse, ConnectionState.NotFound)], readings);
    }

    [Fact]
    public void An_open_channel_without_a_known_device_adds_an_asleep_placeholder_after_the_known_devices()
    {
        var known = KnownOn(ReceiverKey, Mouse, Keyboard);
        var answer = TestData.Status(percent: MousePercent, at: Now);

        var readings = Build(known, AnswersOf((Mouse, answer)), OpenChannels(ReceiverKey, OtherReceiverKey));

        Assert.Equal(
            [
                new DeviceReading(Mouse, answer),
                ReadingWithState(Keyboard, ConnectionState.Asleep),
                ReadingWithState(DeviceInfo.NoDeviceYet, ConnectionState.Asleep),
            ],
            readings);
    }

    [Fact]
    public void No_placeholder_is_added_when_every_open_channel_has_a_known_device()
    {
        var known = KnownOn(ReceiverKey, Mouse);
        known.Seen(Keyboard, new Slot(OtherReceiverKey, FirstSlot));

        var readings = Build(known, NoAnswers(), OpenChannels(ReceiverKey, OtherReceiverKey));

        Assert.Equal(
            [ReadingWithState(Mouse, ConnectionState.Asleep), ReadingWithState(Keyboard, ConnectionState.Asleep)],
            readings);
    }

    private static IReadOnlyList<DeviceReading> Build(
        KnownDevices known,
        IReadOnlyDictionary<DeviceId, BatteryStatus?> statuses,
        IReadOnlySet<string> openChannelKeys) =>
        new DeviceReadingsBuilder().Build(known, statuses, openChannelKeys, Now);

    private static KnownDevices KnownOn(string channelKey, params DeviceInfo[] devices)
    {
        var known = new KnownDevices();
        foreach (var device in devices)
            known.Seen(device, new Slot(channelKey, FirstSlot));
        return known;
    }

    private static Dictionary<DeviceId, BatteryStatus?> NoAnswers() => [];

    private static Dictionary<DeviceId, BatteryStatus?> AnswersOf(params (DeviceInfo Device, BatteryStatus Status)[] answers) =>
        answers.ToDictionary(answer => answer.Device.Id, answer => (BatteryStatus?)answer.Status);

    private static DeviceReading ReadingWithState(DeviceInfo device, ConnectionState state) =>
        new(device, new BatteryStatus(state, null, ChargingState.Unknown, Now));

    private static HashSet<string> OpenChannels(params string[] keys) => [.. keys];
}
