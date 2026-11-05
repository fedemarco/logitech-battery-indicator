using LogitechBattery.HidPp;

namespace LogitechBattery.Tests;

public class HidPpMessageTests
{
    private const byte DeviceIndex = 0x01;
    private const byte FeatureIndex = 0x04;

    // Function 1 in the high nibble, software id 0x0A in the low one.
    private const byte Function1WithSoftwareId = 0x1A;

    private const byte RotatedSoftwareId = 0x03;
    private const byte PreviousSoftwareId = 0x02;
    private const byte Function1WithRotatedId = 0x10 | RotatedSoftwareId;
    private const byte Function1WithPreviousId = 0x10 | PreviousSoftwareId;

    private static byte[] SampleRequest => HidPpMessage.BuildRequest(DeviceIndex, FeatureIndex, function: 1, HidPpMessage.SoftwareId, 0xAB);

    private static byte[] RotatedRequest => HidPpMessage.WithSoftwareId(SampleRequest, RotatedSoftwareId);

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
    public void Long_request_can_get_another_software_id_without_changing_the_original()
    {
        var request = SampleRequest;

        var changed = HidPpMessage.WithSoftwareId(request, 0x03);

        Assert.Equal(0x13, changed[3]);
        Assert.Equal(Function1WithSoftwareId, request[3]);
    }

    [Fact]
    public void Short_request_keeps_its_register_address_when_a_software_id_is_set()
    {
        var request = HidPpMessage.BuildGetRegister(HidPpMessage.ReceiverIndex, register: 0x00);

        Assert.Equal(request, HidPpMessage.WithSoftwareId(request, 0x03));
    }

    [Fact]
    public void Request_with_too_many_parameters_is_rejected() =>
        Assert.Throws<ArgumentException>(() =>
            HidPpMessage.BuildRequest(DeviceIndex, FeatureIndex, 0, HidPpMessage.SoftwareId, new byte[17]));

    [Fact]
    public void Set_register_is_a_short_report_with_the_write_sub_id_register_and_value() =>
        Assert.Equal([0x10, 0xFF, 0x80, 0x00, 0x00, 0x01, 0x00],
            HidPpMessage.BuildSetRegister(HidPpMessage.ReceiverIndex, register: 0x00, [0x00, 0x01, 0x00]));

    [Fact]
    public void Report_with_software_id_zero_from_the_device_and_feature_is_an_event() =>
        Assert.True(HidPpMessage.IsEventFrom(Report(0x11, 0x01, 0x06, 0x00, 0x19, 0x02), deviceIndex: 1, featureIndex: 6));

    [Fact]
    public void Response_to_our_own_request_is_not_an_event() =>
        Assert.False(HidPpMessage.IsEventFrom(Report(0x11, 0x01, 0x06, 0x1A, 0x19, 0x02), deviceIndex: 1, featureIndex: 6));

    [Fact]
    public void Response_to_other_software_is_not_an_event() =>
        Assert.False(HidPpMessage.IsEventFrom(Report(0x11, 0x01, 0x06, 0x1F, 0x19, 0x02), deviceIndex: 1, featureIndex: 6));

    [Fact]
    public void Event_from_another_device_index_is_ignored() =>
        Assert.False(HidPpMessage.IsEventFrom(Report(0x11, 0x02, 0x06, 0x00, 0x19, 0x02), deviceIndex: 1, featureIndex: 6));

    [Fact]
    public void Event_from_another_feature_index_is_ignored() =>
        Assert.False(HidPpMessage.IsEventFrom(Report(0x11, 0x01, 0x07, 0x00, 0x19, 0x02), deviceIndex: 1, featureIndex: 6));

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

    [Fact]
    public void Response_with_the_rotated_software_id_answers_the_rotated_request() =>
        Assert.Equal(HidPpReplyKind.Response,
            HidPpMessage.Classify(RotatedRequest, Report(0x11, DeviceIndex, FeatureIndex, Function1WithRotatedId, 0x55)).Kind);

    [Fact]
    public void Late_reply_with_the_previous_software_id_does_not_answer_the_rotated_request() =>
        Assert.Equal(HidPpReplyKind.None,
            HidPpMessage.Classify(RotatedRequest, Report(0x11, DeviceIndex, FeatureIndex, Function1WithPreviousId, 0x55)).Kind);

    [Fact]
    public void Hidpp2_error_echoing_the_rotated_software_id_answers_the_rotated_request() =>
        Assert.Equal(new HidPpReply(HidPpReplyKind.Error20, 2),
            HidPpMessage.Classify(RotatedRequest, Report(0x11, DeviceIndex, 0xFF, FeatureIndex, Function1WithRotatedId, 0x02)));

    [Fact]
    public void Hidpp1_error_echoing_the_rotated_software_id_answers_the_rotated_request() =>
        Assert.Equal(new HidPpReply(HidPpReplyKind.Error10, 9),
            HidPpMessage.Classify(RotatedRequest, [0x10, DeviceIndex, 0x8F, FeatureIndex, Function1WithRotatedId, 0x09, 0x00]));

    [Fact]
    public void Event_with_software_id_0_never_answers_a_request() =>
        Assert.Equal(HidPpReplyKind.None,
            HidPpMessage.Classify(RotatedRequest, Report(0x11, DeviceIndex, FeatureIndex, 0x10, 0x55)).Kind);

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
