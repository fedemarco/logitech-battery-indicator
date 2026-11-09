namespace LogitechBattery.Battery;

/// <summary>When the tray warns about the battery. The critical level is fixed (<see cref="BatteryAlerts.CriticalPercent"/>).</summary>
public sealed record AlertSettings(int LowPercent, bool FullChargeAlert)
{
    public static IReadOnlyList<int> LowChoices { get; } = [10, 15, 20];

    public static AlertSettings Default { get; } = new(15, FullChargeAlert: true);
}
