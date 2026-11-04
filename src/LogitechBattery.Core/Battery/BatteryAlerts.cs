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
    private const int NewCyclePercent = 25;

    private const int FullPercent = 100;

    private BatteryLevel _lastNotifiedLevel = BatteryLevel.Good; // most severe level already notified
    private bool _seenBelowFull; // the battery was below 100 % while charging or discharging
    private bool _fullAlertSent;

    public BatteryAlert? Update(BatteryStatus status)
    {
        if (status is not { Connection: ConnectionState.Connected, Percent: { } percent })
            return null;

        StartNewCycleIfCharged(status.Charging, percent);

        if (IsFull(status.Charging, percent))
            return FullAlertIfCharged();

        switch (status.Charging)
        {
            case ChargingState.Charging:
                if (percent < FullPercent)
                    _seenBelowFull = true; // charging from below 100 %: a later full is a completed charge
                return null;
            case ChargingState.Discharging:
                _seenBelowFull = percent < FullPercent; // unplugged at 100 % and re-plugged: nothing new to announce
                _fullAlertSent = false;
                return LowOrCriticalAlert(percent);
            default:
                return null;
        }
    }

    private void StartNewCycleIfCharged(ChargingState charging, int percent)
    {
        if (charging is ChargingState.Charging or ChargingState.Full || percent >= NewCyclePercent)
            _lastNotifiedLevel = BatteryLevel.Good;
    }

    // Reported as Full, or as still charging at 100 % (the mouse may not send a separate full state).
    private static bool IsFull(ChargingState charging, int percent) =>
        charging == ChargingState.Full || (charging == ChargingState.Charging && percent >= FullPercent);

    private BatteryAlert? FullAlertIfCharged()
    {
        if (!_seenBelowFull || _fullAlertSent)
            return null;
        _fullAlertSent = true;
        return BatteryAlert.Full;
    }

    private BatteryAlert? LowOrCriticalAlert(int percent)
    {
        if (percent <= BatteryThresholds.Critical && _lastNotifiedLevel != BatteryLevel.Critical)
        {
            _lastNotifiedLevel = BatteryLevel.Critical;
            return BatteryAlert.Critical;
        }
        if (percent <= BatteryThresholds.Low && _lastNotifiedLevel == BatteryLevel.Good)
        {
            _lastNotifiedLevel = BatteryLevel.Low;
            return BatteryAlert.Low;
        }
        return null;
    }
}
