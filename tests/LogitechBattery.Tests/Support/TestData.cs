using LogitechBattery.Battery;

namespace LogitechBattery.Tests;

/// <summary>Builders for the battery values the tests read and record. The defaults are an ordinary connected, discharging mouse.</summary>
internal static class TestData
{
    public static BatteryStatus Status(
        int? percent = 50,
        ChargingState charging = ChargingState.Discharging,
        ConnectionState connection = ConnectionState.Connected,
        DateTimeOffset? at = null) =>
        new(connection, percent, charging, at ?? DateTimeOffset.UnixEpoch);

    public static HistoryRecord Record(
        DateTimeOffset time,
        int? percent = 50,
        ChargingState charging = ChargingState.Discharging,
        ConnectionState connection = ConnectionState.Connected) =>
        new(time, percent, charging, connection);
}
