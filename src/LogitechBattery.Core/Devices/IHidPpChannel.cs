namespace LogitechBattery.Devices;

/// <summary>What device discovery and routing need from a HID++ channel; lets tests replace the real HID streams.</summary>
internal interface IHidPpChannel
{
    string Key { get; }
    int ProductId { get; }

    /// <summary>True once the device is gone or the channel was disposed.</summary>
    bool IsClosed { get; }

    HidPpAnswer Exchange(byte[] request, TimeSpan timeout, CancellationToken cancellationToken);
}
