using LogitechBattery.HidPp;

namespace LogitechBattery.Tests;

public class HidPpMessageTests
{
    private const byte DeviceIndex = 0x01;
    private const byte FeatureIndex = 0x04;

    // Function 1 in the high nibble, software id 0x0A in the low one.
    private const byte Function1WithSoftwareId = 0x1A;

    private const byte OtherDeviceIndex = 0x02;
    private const byte OtherFeatureIndex = 0x05;
    private const byte NoSoftwareId = 0x00;
    private const byte Function1WithNoSoftwareId = 0x10;
    private const byte Function1WithOtherSoftwareId = 0x1F;
    private const byte Hidpp2ErrorMarker = 0xFF;
    private const byte DjReportId = 0x20;

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
    public void Set_register_is_a_short_report_with_the_write_sub_id_register_and_value() =>
        Assert.Equal([0x10, 0xFF, 0x80, 0x00, 0x00, 0x01, 0x00],
            HidPpMessage.BuildSetRegister(HidPpMessage.ReceiverIndex, register: 0x00, [0x00, 0x01, 0x00]));

    [Theory]
    [InlineData(DeviceIndex, FeatureIndex, NoSoftwareId, true)]
    [InlineData(DeviceIndex, FeatureIndex, Function1WithSoftwareId, false)] // response to our own request
    [InlineData(DeviceIndex, FeatureIndex, Function1WithOtherSoftwareId, false)] // response to other software
    [InlineData(OtherDeviceIndex, FeatureIndex, NoSoftwareId, false)]
    [InlineData(DeviceIndex, OtherFeatureIndex, NoSoftwareId, false)]
    public void Only_a_report_with_software_id_zero_from_the_device_and_feature_is_an_event(byte deviceIndex, byte featureIndex, byte functionAndSoftwareId, bool isEvent)
    {
        var report = Report(HidPpMessage.LongId, deviceIndex, featureIndex, functionAndSoftwareId, 0x19, 0x02);

        Assert.Equal(isEvent, HidPpMessage.IsEventFrom(report, DeviceIndex, FeatureIndex));
    }

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

    [Theory]
    [InlineData(HidPpMessage.LongId, OtherDeviceIndex, FeatureIndex, Function1WithSoftwareId)]
    [InlineData(HidPpMessage.LongId, DeviceIndex, OtherFeatureIndex, Function1WithSoftwareId)] // a notification
    [InlineData(HidPpMessage.LongId, DeviceIndex, FeatureIndex, Function1WithNoSoftwareId)] // an event never answers a request
    [InlineData(HidPpMessage.LongId, DeviceIndex, Hidpp2ErrorMarker, OtherFeatureIndex)] // error for another feature
    [InlineData(DjReportId, DeviceIndex, FeatureIndex, Function1WithSoftwareId)] // not a HID++ report
    public void Unrelated_reports_are_not_an_answer(byte reportId, byte deviceIndex, byte featureOrMarker, byte functionAndSoftwareId)
    {
        var report = Report(reportId, deviceIndex, featureOrMarker, functionAndSoftwareId, 0x00, 0x00);

        Assert.Equal(HidPpReplyKind.None, HidPpMessage.Classify(SampleRequest, report).Kind);
    }
}
