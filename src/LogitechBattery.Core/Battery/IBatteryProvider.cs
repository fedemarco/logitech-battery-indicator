namespace LogitechBattery.Battery;

public interface IBatteryProvider
{
    Task<IReadOnlyList<DeviceReading>> ReadAsync(CancellationToken cancellationToken = default);
}
