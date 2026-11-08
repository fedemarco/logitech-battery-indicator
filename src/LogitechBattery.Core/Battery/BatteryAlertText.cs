namespace LogitechBattery.Battery;

/// <summary>The title and text of the notification for a <see cref="BatteryAlert"/>.</summary>
public static class BatteryAlertText
{
    public static (string Title, string Text) For(BatteryAlert alert, int? percent) => alert switch
    {
        BatteryAlert.Critical => ("Mouse battery critical", $"{percent}% left."),
        BatteryAlert.Low => ("Mouse battery low", $"{percent}% left. Charge it soon."),
        BatteryAlert.Full => ("Mouse fully charged", $"{percent}%."),
        _ => throw new ArgumentOutOfRangeException(nameof(alert), alert, null),
    };
}
