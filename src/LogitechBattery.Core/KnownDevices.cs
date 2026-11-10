using LogitechBattery.Battery;

namespace LogitechBattery;

/// <summary>The devices seen this session in first-seen order, each with the slot it was last seen on.</summary>
internal sealed class KnownDevices
{
    private readonly List<DeviceInfo> _devices = [];
    private readonly Dictionary<DeviceId, Slot> _lastSlots = [];

    public bool IsEmpty => _devices.Count == 0;

    public IReadOnlyList<DeviceInfo> InFirstSeenOrder => _devices;

    /// <summary>Records the device (and its latest name); true if it is new or was last seen on another slot.</summary>
    public bool Seen(DeviceInfo device, Slot slot)
    {
        var position = _devices.FindIndex(known => known.Id == device.Id);
        if (position < 0)
            _devices.Add(device);
        else
            _devices[position] = device;

        var isNews = !_lastSlots.TryGetValue(device.Id, out var lastSlot) || lastSlot != slot;
        _lastSlots[device.Id] = slot;
        return isNews;
    }

    /// <summary>Asleep while the channel it was last seen on is still open (a receiver hears it wake up); NotFound once that channel is gone.</summary>
    public ConnectionState StateWhenNotAnswering(DeviceInfo device, IReadOnlySet<string> openChannelKeys) =>
        openChannelKeys.Contains(_lastSlots[device.Id].ChannelKey) ? ConnectionState.Asleep : ConnectionState.NotFound;
}
