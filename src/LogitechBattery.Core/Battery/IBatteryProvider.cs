namespace LogitechBattery.Battery;

public interface IBatteryProvider
{
    Task<BatteryStatus> ReadAsync(CancellationToken cancellationToken = default);
}
