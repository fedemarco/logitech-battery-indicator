namespace LogitechBattery.Battery;

public enum BatteryLevel
{
    /// <summary>Green.</summary>
    Good,

    /// <summary>Orange.</summary>
    Low,

    /// <summary>Red.</summary>
    Critical,
}

public static class BatteryThresholds
{
    /// <summary>Percent at or above which the level is <see cref="BatteryLevel.Good"/>.</summary>
    public const int Good = 50;

    /// <summary>Percent at or above which the level is <see cref="BatteryLevel.Low"/>; below it is Critical.</summary>
    public const int Low = 15;
}
