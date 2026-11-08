using LogitechBattery.Battery;

namespace LogitechBattery.Tests;

public class BatteryStatusTests
{
    [Theory]
    [InlineData(4, BatteryLevel.Critical)]
    [InlineData(5, BatteryLevel.Critical)]
    [InlineData(6, BatteryLevel.Low)]
    [InlineData(15, BatteryLevel.Low)]
    [InlineData(16, BatteryLevel.Good)]
    [InlineData(100, BatteryLevel.Good)]
    public void Level_changes_at_the_critical_and_low_thresholds(int percent, BatteryLevel expected) =>
        Assert.Equal(expected, TestData.Status(percent).Level);

    [Fact]
    public void Level_is_unknown_without_percent() =>
        Assert.Null(TestData.Status(null, ChargingState.Unknown).Level);

    [Theory]
    [InlineData(ConnectionState.Asleep, null, ChargingState.Unknown, "Mouse asleep")]
    [InlineData(ConnectionState.Asleep, 50, ChargingState.Charging, "Mouse asleep")]
    [InlineData(ConnectionState.NotFound, null, ChargingState.Unknown, "Mouse not found")]
    [InlineData(ConnectionState.NotFound, 50, ChargingState.Discharging, "Mouse not found")]
    [InlineData(ConnectionState.Connected, 60, ChargingState.Charging, "Charging · 60%")]
    [InlineData(ConnectionState.Connected, null, ChargingState.Charging, "Charging")]
    [InlineData(ConnectionState.Connected, 100, ChargingState.Full, "Full · 100%")]
    [InlineData(ConnectionState.Connected, null, ChargingState.Full, "Full")]
    [InlineData(ConnectionState.Connected, 85, ChargingState.Discharging, "85% · Discharging")]
    [InlineData(ConnectionState.Connected, null, ChargingState.Discharging, "Battery unknown")]
    [InlineData(ConnectionState.Connected, 70, ChargingState.Unknown, "70%")]
    [InlineData(ConnectionState.Connected, null, ChargingState.Unknown, "Battery unknown")]
    public void Display_string_describes_connection_charging_and_percent(ConnectionState connection, int? percent, ChargingState charging, string expected) =>
        Assert.Equal(expected, TestData.Status(percent, charging, connection).ToDisplayString());
}
