namespace LogitechBattery;

/// <summary>Where a device answers: a channel and a device index on it.</summary>
internal sealed record Slot(string ChannelKey, byte DeviceIndex);
