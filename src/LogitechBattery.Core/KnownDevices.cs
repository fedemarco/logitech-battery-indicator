using LogitechBattery.Battery;

namespace LogitechBattery;

/// <summary>The devices seen this session in first-seen order, each with the channel it was last seen on.</summary>
internal sealed class KnownDevices
{
    private readonly List<DeviceInfo> _devices = [];
    private readonly Dictionary<DeviceId, string> _lastChannelKeys = [];

    public bool IsEmpty => _devices.Count == 0;

    public IReadOnlyList<DeviceInfo> InFirstSeenOrder => _devices;

    public void Seen(DeviceInfo device, string channelKey)
    {
        if (!_lastChannelKeys.ContainsKey(device.Id))
            _devices.Add(device);
        _lastChannelKeys[device.Id] = channelKey;
    }

    /// <summary>Asleep while the channel it was last seen on is still open (a receiver hears it wake up); NotFound once that channel is gone.</summary>
    public ConnectionState StateWhenNotAnswering(DeviceInfo device, IReadOnlySet<string> openChannelKeys) =>
        openChannelKeys.Contains(_lastChannelKeys[device.Id]) ? ConnectionState.Asleep : ConnectionState.NotFound;
}
