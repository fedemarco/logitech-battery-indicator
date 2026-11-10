namespace LogitechBattery.Battery;

/// <summary>The title and text of the notification for a <see cref="BatteryAlert"/>.</summary>
public static class BatteryAlertText
{
    public static (string Title, string Text) For(BatteryAlert alert, int? percent, string deviceName) => alert switch
    {
        BatteryAlert.Critical => ($"{deviceName} battery critical", $"{percent}% left."),
        BatteryAlert.Low => ($"{deviceName} battery low", $"{percent}% left. Charge it soon."),
        BatteryAlert.Full => ($"{deviceName} fully charged", $"{percent}%."),
        _ => throw new ArgumentOutOfRangeException(nameof(alert), alert, null),
    };
}
