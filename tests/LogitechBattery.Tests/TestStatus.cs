using LogitechBattery.Battery;

namespace LogitechBattery.Tests;

internal static class TestStatus
{
    public static BatteryStatus Reading(ConnectionState connection, int? percent, ChargingState charging) =>
        new(connection, percent, charging, DateTimeOffset.UnixEpoch);
}
