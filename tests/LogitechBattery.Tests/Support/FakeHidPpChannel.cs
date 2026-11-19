using LogitechBattery.HidPp;

namespace LogitechBattery.Tests;

/// <summary>A HID++ channel without hardware: each device index answers like the scripted <see cref="FakeDevice"/>, an index without one answers nothing.</summary>
internal sealed class FakeHidPpChannel(string key = "fake-channel", int productId = 0xC54D) : IHidPpChannel
{
    private readonly Dictionary<byte, FakeDevice> _devices = [];
    private readonly Dictionary<byte, int> _requestCounts = [];

    public string Key { get; } = key;
    public int ProductId { get; } = productId;
    public bool IsClosed { get; set; }

    /// <summary>When set, every request throws it, like a channel whose device was unplugged.</summary>
    public Exception? Failure { get; set; }

    public FakeHidPpChannel With(byte deviceIndex, FakeDevice device)
    {
        _devices[deviceIndex] = device;
        return this;
    }

    public int RequestsTo(byte deviceIndex) => _requestCounts.GetValueOrDefault(deviceIndex);

    public HidPpAnswer Exchange(byte[] request, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deviceIndex = request[HidPpMessage.DeviceIndexOffset];
        _requestCounts[deviceIndex] = RequestsTo(deviceIndex) + 1;
        if (Failure is not null)
            throw Failure;
        return _devices.TryGetValue(deviceIndex, out var device) ? device.Answer(request) : HidPpAnswer.Nothing;
    }
}
