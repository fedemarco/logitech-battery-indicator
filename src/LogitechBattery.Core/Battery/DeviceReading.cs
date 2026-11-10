namespace LogitechBattery.Battery;

/// <summary>One device and its battery status; a null <paramref name="Status"/> means reading this device failed.</summary>
public sealed record DeviceReading(DeviceInfo Device, BatteryStatus? Status);
