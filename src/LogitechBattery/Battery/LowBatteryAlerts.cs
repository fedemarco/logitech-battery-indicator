namespace LogitechBattery.Battery;

public enum BatteryAlert
{
    Low,
    Critical,
}

/// <summary>Decides when a reading deserves a low-battery notification: at most one per reading, each level once per discharge cycle.</summary>
public sealed class LowBatteryAlerts
{
    /// <summary>Percent at or above which a new discharge cycle is assumed (hysteresis against bouncing readings).</summary>
    private const int ResetPercent = 25;

    private BatteryLevel _notified = BatteryLevel.Good; // most severe level already notified

    public BatteryAlert? Update(BatteryStatus status)
    {
        if (status is not { Connection: ConnectionState.Connected, Percent: { } percent })
            return null;

        if (status.Charging is ChargingState.Charging or ChargingState.Full || percent >= ResetPercent)
        {
            _notified = BatteryLevel.Good;
            return null;
        }
        if (status.Charging != ChargingState.Discharging)
            return null;

        if (percent <= BatteryThresholds.Critical && _notified != BatteryLevel.Critical)
        {
            _notified = BatteryLevel.Critical;
            return BatteryAlert.Critical;
        }
        if (percent <= BatteryThresholds.Low && _notified == BatteryLevel.Good)
        {
            _notified = BatteryLevel.Low;
            return BatteryAlert.Low;
        }
        return null;
    }
}
