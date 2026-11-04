namespace LogitechBattery.Battery;

public enum BatteryAlert
{
    Low,
    Critical,
    Full,
}

/// <summary>Decides when a reading deserves a notification: low/critical once per discharge cycle (at most one per reading),
/// full once per charge, if the battery was seen below 100 % before (so a full mouse at startup stays silent).</summary>
public sealed class BatteryAlerts
{
    /// <summary>Percent at or above which a new discharge cycle is assumed (hysteresis against bouncing readings).</summary>
    private const int ResetPercent = 25;

    private BatteryLevel _notified = BatteryLevel.Good; // most severe level already notified
    private bool _fullArmed; // set once the battery was seen below 100 % (charging or discharging)
    private bool _fullNotified;

    public BatteryAlert? Update(BatteryStatus status)
    {
        if (status is not { Connection: ConnectionState.Connected, Percent: { } percent })
            return null;

        if (status.Charging is ChargingState.Charging or ChargingState.Full || percent >= ResetPercent)
            _notified = BatteryLevel.Good;

        // Full: reported as Full, or as still charging at 100 % (the mouse may not send a separate full state).
        if (status.Charging == ChargingState.Full || (status.Charging == ChargingState.Charging && percent >= 100))
        {
            if (!_fullArmed || _fullNotified)
                return null;
            _fullNotified = true;
            return BatteryAlert.Full;
        }
        if (status.Charging == ChargingState.Charging)
        {
            if (percent < 100)
                _fullArmed = true; // charging from below 100 %: a later full is a completed charge
            return null;
        }
        if (status.Charging == ChargingState.Discharging)
        {
            _fullArmed = percent < 100; // unplugged at 100 % and re-plugged: nothing new to announce
            _fullNotified = false;
        }
        if (percent >= ResetPercent)
            return null;
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
