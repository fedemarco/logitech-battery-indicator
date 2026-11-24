using LogitechBattery.Battery;
using LogitechBattery.Devices;
using LogitechBattery.HidPp;

namespace LogitechBattery.Tests;

public class HidPpBatteryReadTests
{
    private const byte DeviceIndex = 1;
    private const byte BatteryFeatureIndex = 0x08;
    private const byte ErrorCode = 0x05;
    private const byte BatteryPercent = 80;
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;

    [Fact]
    public void An_error_code_is_a_failed_read()
    {
        var read = ReadAnswerOnFakeRoute(new HidPpAnswer(null, ErrorCode));

        Assert.Equal(BatteryStatusRequest.RouteOutcome.Failed, read.Outcome);
    }

    [Fact]
    public void No_answer_is_no_answer()
    {
        var read = ReadAnswerOnFakeRoute(HidPpAnswer.Nothing);

        Assert.Equal(BatteryStatusRequest.RouteOutcome.NoAnswer, read.Outcome);
    }

    [Fact]
    public void A_malformed_answer_is_a_failed_read()
    {
        var read = ReadAnswerOnFakeRoute(new HidPpAnswer(AnswerWithoutStatusBytes, null));

        Assert.Equal(BatteryStatusRequest.RouteOutcome.Failed, read.Outcome);
    }

    [Fact]
    public void A_valid_answer_gives_the_status()
    {
        var read = ReadAnswerOnFakeRoute(new HidPpAnswer(StatusReport(BatteryPercent, 0, 0, 0), null));

        Assert.Equal(BatteryStatusRequest.RouteOutcome.Answered, read.Outcome);
        Assert.Equal(BatteryPercent, read.Status!.Percent);
    }

    [Fact]
    public void An_unexpected_exception_from_the_device_is_a_failed_read_of_that_device()
    {
        var read = ReadStatusOn(new FakeHidPpChannel { Failure = new InvalidOperationException() });

        Assert.Equal(BatteryStatusRequest.RouteOutcome.Failed, read.Outcome);
    }

    [Fact]
    public void An_IOException_from_the_channel_is_a_lost_channel()
    {
        var read = ReadStatusOn(new FakeHidPpChannel { Failure = new IOException() });

        Assert.Equal(BatteryStatusRequest.RouteOutcome.ChannelLost, read.Outcome);
    }

    [Fact]
    public void A_cancelled_read_is_not_caught()
    {
        var channel = new FakeHidPpChannel { Failure = new OperationCanceledException() };

        Assert.Throws<OperationCanceledException>(() => ReadStatusOn(channel));
    }

    private static byte[] AnswerWithoutStatusBytes => StatusReport();

    private static BatteryStatusRequest.RouteRead ReadAnswerOnFakeRoute(HidPpAnswer answer) =>
        BatteryStatusRequest.ReadAnswer(RouteOn(new FakeHidPpChannel()), answer, Now);

    private static BatteryStatusRequest.RouteRead ReadStatusOn(FakeHidPpChannel channel) =>
        BatteryStatusRequest.ReadStatus(RouteOn(channel), Now, CancellationToken.None);

    private static BatteryTarget RouteOn(FakeHidPpChannel channel) =>
        new(channel, DeviceIndex, BatteryFeatureIndex, new DeviceInfo(DeviceId.Parse("1A2B3C4D"), "Mouse"));

    private static byte[] StatusReport(params byte[] statusParams)
    {
        var report = new byte[HidPpMessage.HeaderLength + statusParams.Length];
        statusParams.CopyTo(report, HidPpMessage.ParamsOffset);
        return report;
    }
}
