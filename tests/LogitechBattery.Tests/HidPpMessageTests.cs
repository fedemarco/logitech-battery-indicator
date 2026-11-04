using LogitechBattery.HidPp;

namespace LogitechBattery.Tests;

public class HidPpMessageTests
{
    private const byte DeviceIndex = 0x01;
    private const byte FeatureIndex = 0x04;

    // Function 1 in the high nibble, software id 0x0A in the low one.
    private const byte Function1WithSoftwareId = 0x1A;

    private static byte[] SampleRequest => HidPpMessage.BuildRequest(DeviceIndex, FeatureIndex, function: 1, HidPpMessage.SoftwareId, 0xAB);

    private static byte[] Report(params byte[] bytes)
    {
        var report = new byte[HidPpMessage.LongLength];
        bytes.CopyTo(report, 0);
        return report;
    }

    [Fact]
    public void Request_is_a_zero_padded_long_report_with_header_then_parameters()
    {
        var request = SampleRequest;

        Assert.Equal(20, request.Length);
        Assert.Equal([0x11, DeviceIndex, FeatureIndex, Function1WithSoftwareId, 0xAB, 0x00], request[..6]);
    }

    [Fact]
    public void Request_with_too_many_parameters_is_rejected() =>
        Assert.Throws<ArgumentException>(() =>
            HidPpMessage.BuildRequest(DeviceIndex, FeatureIndex, 0, HidPpMessage.SoftwareId, new byte[17]));

    [Fact]
    public void Get_register_is_a_short_report_with_the_read_sub_id_and_the_register_address() =>
        Assert.Equal([0x10, 0xFF, 0x81, 0x00, 0x00, 0x00, 0x00],
            HidPpMessage.BuildGetRegister(HidPpMessage.ReceiverIndex, register: 0x00));

    [Fact]
    public void Ping_asks_the_root_feature_for_the_protocol_version_and_carries_the_echo_byte() =>
        Assert.Equal([0x11, DeviceIndex, 0x00, Function1WithSoftwareId, 0x00, 0x00, 0x5A],
            HidPpMessage.BuildPing(DeviceIndex)[..7]);

    [Fact]
    public void GetFeature_asks_the_root_feature_for_the_index_of_a_feature_id() =>
        Assert.Equal([0x11, DeviceIndex, 0x00, 0x0A, 0x10, 0x04],
            HidPpMessage.BuildGetFeature(DeviceIndex, 0x1004)[..6]);

    [Fact]
    public void Params_start_after_the_header() =>
        Assert.Equal([0xAB, 0x00], HidPpMessage.ParamsOf(SampleRequest)[..2]);

    [Fact]
    public void Same_feature_and_function_from_same_device_is_the_response()
    {
        var reply = HidPpMessage.Classify(SampleRequest, Report(0x11, DeviceIndex, FeatureIndex, Function1WithSoftwareId, 0x55));

        Assert.Equal(HidPpReplyKind.Response, reply.Kind);
    }

    [Fact]
    public void Error_report_for_the_request_is_a_hidpp2_error_with_its_code()
    {
        var reply = HidPpMessage.Classify(SampleRequest, Report(0x11, DeviceIndex, 0xFF, FeatureIndex, Function1WithSoftwareId, 0x02));

        Assert.Equal(new HidPpReply(HidPpReplyKind.Error20, 2), reply);
    }

    [Fact]
    public void Short_error_report_for_the_request_is_a_hidpp1_error_with_its_code()
    {
        var reply = HidPpMessage.Classify(SampleRequest, [0x10, DeviceIndex, 0x8F, FeatureIndex, Function1WithSoftwareId, 0x09, 0x00]);

        Assert.Equal(new HidPpReply(HidPpReplyKind.Error10, 9), reply);
    }

    [Theory]
    [InlineData(0x11, 0x02, 0x04, 0x1A)] // other device
    [InlineData(0x11, 0x01, 0x05, 0x1A)] // other feature (notification)
    [InlineData(0x11, 0x01, 0x04, 0x10)] // other function/swId
    [InlineData(0x11, 0x01, 0xFF, 0x05)] // error for another feature
    [InlineData(0x20, 0x01, 0x04, 0x1A)] // not HID++ (DJ) report
    public void Unrelated_reports_are_not_an_answer(int reportId, int device, int feature, int functionAndSoftwareId)
    {
        var report = Report((byte)reportId, (byte)device, (byte)feature, (byte)functionAndSoftwareId, 0x00, 0x00);

        Assert.Equal(HidPpReplyKind.None, HidPpMessage.Classify(SampleRequest, report).Kind);
    }

    [Fact]
    public void Hex_has_lowercase_bytes_separated_by_spaces() =>
        Assert.Equal("11 0a ff", HidPpMessage.ToHex([0x11, 0x0A, 0xFF]));
}
