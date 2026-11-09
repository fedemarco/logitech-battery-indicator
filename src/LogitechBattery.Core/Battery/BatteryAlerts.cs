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
    public const int CriticalPercent = 5;

    /// <summary>A new discharge cycle is assumed this far above the low level (hysteresis against bouncing readings).</summary>
    private const int NewCycleMarginPercent = 10;

    private BatteryLevel _lastNotifiedLevel = BatteryLevel.Good; // most severe level already notified
    private bool _seenBelowFull; // the battery was below 100 % while charging or discharging
    private bool _fullAlertSent;

    public BatteryAlert? Update(BatteryStatus status, AlertSettings settings)
    {
        if (status is not { Connection: ConnectionState.Connected, Percent: { } percent })
            return null;

        StartNewCycleIfCharged(status.Charging, percent, settings.LowPercent);

        if (IsFull(status.Charging, percent))
            return FullAlertIfCharged(settings.FullChargeAlert);

        switch (status.Charging)
        {
            case ChargingState.Charging:
                if (percent < BatteryStatus.FullPercent)
                    _seenBelowFull = true; // charging from below 100 %: a later full is a completed charge
                return null;
            case ChargingState.Discharging:
                _seenBelowFull = percent < BatteryStatus.FullPercent; // unplugged at 100 % and re-plugged: nothing new to announce
                _fullAlertSent = false;
                return LowOrCriticalAlert(percent, settings.LowPercent);
            default:
                return null;
        }
    }

    private void StartNewCycleIfCharged(ChargingState charging, int percent, int lowPercent)
    {
        if (charging is ChargingState.Charging or ChargingState.Full || percent >= lowPercent + NewCycleMarginPercent)
            _lastNotifiedLevel = BatteryLevel.Good;
    }

    // Reported as Full, or as still charging at 100 % (the mouse may not send a separate full state).
    private static bool IsFull(ChargingState charging, int percent) =>
        charging == ChargingState.Full || (charging == ChargingState.Charging && percent >= BatteryStatus.FullPercent);

    // The state changes even when the alert is switched off, so switching it on later does not alert for a charge that is over.
    private BatteryAlert? FullAlertIfCharged(bool fullChargeAlertOn)
    {
        if (!_seenBelowFull || _fullAlertSent)
            return null;
        _fullAlertSent = true;
        return fullChargeAlertOn ? BatteryAlert.Full : null;
    }

    private BatteryAlert? LowOrCriticalAlert(int percent, int lowPercent)
    {
        if (percent <= CriticalPercent && _lastNotifiedLevel != BatteryLevel.Critical)
        {
            _lastNotifiedLevel = BatteryLevel.Critical;
            return BatteryAlert.Critical;
        }
        if (percent <= lowPercent && _lastNotifiedLevel == BatteryLevel.Good)
        {
            _lastNotifiedLevel = BatteryLevel.Low;
            return BatteryAlert.Low;
        }
        return null;
    }
}
