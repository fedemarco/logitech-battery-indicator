namespace LogitechBattery.Battery;

public sealed record DeviceInfo(DeviceId Id, string Name)
{
    /// <summary>
    /// Stands in for a device while none is known yet (the mouse is off at startup). It carries the mouse name only so the
    /// tray shows what it showed before multi-device support, and its id (product 0, slot 0) only keys the logged connection
    /// state. It is not a real device and must never get a history file.
    /// </summary>
    public static DeviceInfo NoDeviceYet { get; } = new(DeviceId.FromRoute(productId: 0, deviceIndex: 0), "PRO X2 SUPERSTRIKE");
}
