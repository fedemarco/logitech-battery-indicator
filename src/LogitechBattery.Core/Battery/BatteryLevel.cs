namespace LogitechBattery.Battery;

public enum BatteryLevel
{
    Good,

    Low,

    Critical,
}

/// <summary>Fixed for now; meant to become configurable.</summary>
public static class BatteryThresholds
{
    /// <summary>Percent at or below which the level is <see cref="BatteryLevel.Low"/>.</summary>
    public const int Low = 15;

    /// <summary>Percent at or below which the level is <see cref="BatteryLevel.Critical"/>.</summary>
    public const int Critical = 5;
}
