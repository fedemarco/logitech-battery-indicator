using LogitechBattery.HidPp;

namespace LogitechBattery.Tests;

public class HidPpMessageTests
{
    private static byte[] Req => HidPpMessage.BuildRequest(0x01, 0x04, 1, 0x0A, 0xAB);

    private static byte[] Report(params byte[] bytes)
    {
        var r = new byte[20];
        bytes.CopyTo(r, 0);
        return r;
    }

    [Fact]
    public void BuildRequest_LaysOutBytes()
    {
        var r = Req;
        Assert.Equal(20, r.Length);
        Assert.Equal(new byte[] { 0x11, 0x01, 0x04, 0x1A, 0xAB, 0x00 }, r[..6]);
    }

    [Fact]
    public void Response_Matches()
    {
        var reply = HidPpMessage.Classify(Req, Report(0x11, 0x01, 0x04, 0x1A, 0x55));
        Assert.Equal(HidPpReplyKind.Response, reply.Kind);
    }

    [Fact]
    public void Error20_ReturnsCode()
    {
        var reply = HidPpMessage.Classify(Req, Report(0x11, 0x01, 0xFF, 0x04, 0x1A, 0x02));
        Assert.Equal(new HidPpReply(HidPpReplyKind.Error20, 2), reply);
    }

    [Fact]
    public void Error10_ShortReport_ReturnsCode()
    {
        var reply = HidPpMessage.Classify(Req, new byte[] { 0x10, 0x01, 0x8F, 0x04, 0x1A, 0x09, 0x00 });
        Assert.Equal(new HidPpReply(HidPpReplyKind.Error10, 9), reply);
    }

    [Theory]
    [InlineData(0x11, 0x02, 0x04, 0x1A)] // other device
    [InlineData(0x11, 0x01, 0x05, 0x1A)] // other feature (notification)
    [InlineData(0x11, 0x01, 0x04, 0x10)] // other function/swId
    [InlineData(0x11, 0x01, 0xFF, 0x05)] // error for another feature
    [InlineData(0x20, 0x01, 0x04, 0x1A)] // not HID++ (DJ) report
    public void Unrelated_IsIgnored(int id, int dev, int feat, int fn)
    {
        var reply = HidPpMessage.Classify(Req, Report((byte)id, (byte)dev, (byte)feat, (byte)fn, 0x00, 0x00));
        Assert.Equal(HidPpReplyKind.None, reply.Kind);
    }

    [Fact]
    public void ToHex_SpacesBytes() =>
        Assert.Equal("11 0a ff", HidPpMessage.ToHex(new byte[] { 0x11, 0x0A, 0xFF }));
}
