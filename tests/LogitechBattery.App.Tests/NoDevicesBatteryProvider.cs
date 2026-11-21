using LogitechBattery.Battery;

namespace LogitechBattery.App.Tests;

/// <summary>A provider that is not the simulator and reports no devices.</summary>
internal sealed class NoDevicesBatteryProvider : IBatteryProvider
{
    public Task<IReadOnlyList<DeviceReading>> ReadAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<DeviceReading>>([]);
}
