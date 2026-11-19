using System.Text;
using LogitechBattery.HidPp;

namespace LogitechBattery.Tests;

/// <summary>A scripted HID++ 2.0 device. It answers the requests of device discovery; the builder methods choose what it has.</summary>
internal sealed class FakeDevice
{
    private const byte DeviceInfoFeatureIndex = 0x03;
    private const byte NameFeatureIndex = 0x05;
    private const byte BatteryFeatureIndex = 0x08;
    private const byte UnitIdEntityCount = 1;
    private const int NameChunkLength = 16;

    private readonly Dictionary<ushort, byte> _featureIndexes = [];
    private bool _answersFeatureLookups = true;
    private Exception? _failure;
    private string _name = "";
    private Func<byte[], HidPpAnswer>? _deviceInfoAnswer;

    public static FakeDevice Awake() => new();

    public FakeDevice WithUnifiedBattery() => WithFeature(UnifiedBattery.FeatureId, BatteryFeatureIndex);

    public FakeDevice WithName(string name)
    {
        _name = name;
        return WithFeature(DeviceName.FeatureId, NameFeatureIndex);
    }

    public FakeDevice WithUnitId(string unitId) =>
        WithDeviceInfo(request => LongResponse(request, [UnitIdEntityCount, .. Convert.FromHexString(unitId)]));

    public FakeDevice WithDeviceInfoError(byte errorCode) => WithDeviceInfo(_ => new HidPpAnswer(null, errorCode));

    /// <summary>The getDeviceInfo answer is a short report, so its parameters are too few to hold a unit ID.</summary>
    public FakeDevice WithTooShortDeviceInfo() => WithDeviceInfo(request => ShortResponse(request, [UnitIdEntityCount, 0xAA, 0xBB]));

    /// <summary>The device answers the ping but never says which feature index a feature has.</summary>
    public FakeDevice WithoutFeatureLookupAnswers()
    {
        _answersFeatureLookups = false;
        return this;
    }

    public FakeDevice ThrowingOnEveryRequest(Exception failure)
    {
        _failure = failure;
        return this;
    }

    public HidPpAnswer Answer(byte[] request)
    {
        if (_failure is not null)
            throw _failure;

        var featureIndex = request[HidPpMessage.FeatureOrSubIdOffset];
        var function = request[HidPpMessage.HeaderLength - 1] >> 4;
        return featureIndex switch
        {
            HidPpMessage.RootFeatureIndex => AnswerRoot(request, function),
            NameFeatureIndex => AnswerName(request, function),
            DeviceInfoFeatureIndex when _deviceInfoAnswer is not null => _deviceInfoAnswer(request),
            _ => HidPpAnswer.Nothing,
        };
    }

    private FakeDevice WithFeature(ushort featureId, byte featureIndex)
    {
        _featureIndexes[featureId] = featureIndex;
        return this;
    }

    private FakeDevice WithDeviceInfo(Func<byte[], HidPpAnswer> answer)
    {
        _deviceInfoAnswer = answer;
        return WithFeature(DeviceInformation.FeatureId, DeviceInfoFeatureIndex);
    }

    private HidPpAnswer AnswerRoot(byte[] request, int function)
    {
        if (function == HidPpMessage.PingFunction)
            return LongResponse(request, [0, 0, HidPpMessage.PingEcho]);
        if (!_answersFeatureLookups)
            return HidPpAnswer.Nothing;

        var requestedFeatureId = (ushort)((request[HidPpMessage.ParamsOffset] << 8) | request[HidPpMessage.ParamsOffset + 1]);
        return LongResponse(request, [_featureIndexes.GetValueOrDefault(requestedFeatureId)]); // index 0 means "not supported"
    }

    private HidPpAnswer AnswerName(byte[] request, int function)
    {
        if (function == DeviceName.GetCountFunction)
            return LongResponse(request, [(byte)_name.Length]);

        var position = request[HidPpMessage.ParamsOffset];
        var chunk = Encoding.ASCII.GetBytes(_name.Skip(position).Take(NameChunkLength).ToArray());
        return LongResponse(request, chunk);
    }

    private static HidPpAnswer LongResponse(byte[] request, byte[] parameters) => Response(request, HidPpMessage.LongId, HidPpMessage.LongLength, parameters);

    private static HidPpAnswer ShortResponse(byte[] request, byte[] parameters) => Response(request, HidPpMessage.ShortId, HidPpMessage.ShortLength, parameters);

    private static HidPpAnswer Response(byte[] request, byte reportId, int reportLength, byte[] parameters)
    {
        var report = new byte[reportLength];
        request[..HidPpMessage.HeaderLength].CopyTo(report, 0);
        report[HidPpMessage.ReportIdOffset] = reportId;
        parameters.CopyTo(report, HidPpMessage.ParamsOffset);
        return new HidPpAnswer(report, null);
    }
}
