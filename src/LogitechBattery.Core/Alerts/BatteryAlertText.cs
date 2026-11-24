using LogitechBattery.Battery;

namespace LogitechBattery.Alerts;

/// <summary>One balloon: its title, its text and whether it warns (low or critical) or only informs (fully charged).</summary>
public sealed record BalloonContent(string Title, string Text, bool IsWarning);

/// <summary>The title and text of the notification for a <see cref="BatteryAlert"/>, or for all alerts of one read.</summary>
public static class BatteryAlertText
{
    private const string LineSeparator = "\n";

    public static (string Title, string Text) For(BatteryAlert alert, int? percent, string deviceName) => alert switch
    {
        BatteryAlert.Critical => ($"{deviceName} battery critical", $"{percent}% left."),
        BatteryAlert.Low => ($"{deviceName} battery low", $"{percent}% left. Charge it soon."),
        BatteryAlert.Full => ($"{deviceName} fully charged", $"{percent}%."),
        _ => throw new ArgumentOutOfRangeException(nameof(alert), alert, null),
    };

    // Windows shows one balloon per tray icon: a new one replaces the old, so one read gives one balloon.
    // shortcut: Windows cuts the balloon text above 255 characters (about 9 lines); upgrade if a setup can alert more devices at once.
    /// <summary>One alert keeps its own text. Several are combined into one balloon, the most severe first.</summary>
    public static BalloonContent ForRead(IReadOnlyList<DeviceAlert> alerts)
    {
        if (alerts.Count == 0)
            throw new ArgumentException("A balloon needs at least one alert.", nameof(alerts));

        if (alerts.Count == 1)
            return ForSingleAlert(alerts[0]);

        var mostSevereFirst = alerts.OrderBy(deviceAlert => SeverityRank(deviceAlert.Alert)).ToList();
        var title = CombinedTitle(mostSevereFirst[0].Alert);
        var lines = mostSevereFirst.Select(LineFor).ToList();
        return new BalloonContent(title, string.Join(LineSeparator, lines), IsWarning(mostSevereFirst[0].Alert));
    }

    private static BalloonContent ForSingleAlert(DeviceAlert deviceAlert)
    {
        var (title, text) = For(deviceAlert.Alert, deviceAlert.Percent, deviceAlert.DeviceName);
        return new BalloonContent(title, text, IsWarning(deviceAlert.Alert));
    }

    private static bool IsWarning(BatteryAlert alert) => alert != BatteryAlert.Full;

    private static int SeverityRank(BatteryAlert alert) => alert switch
    {
        BatteryAlert.Critical => 0,
        BatteryAlert.Low => 1,
        BatteryAlert.Full => 2,
        _ => throw new ArgumentOutOfRangeException(nameof(alert), alert, null),
    };

    private static string CombinedTitle(BatteryAlert mostSevereAlert) => mostSevereAlert switch
    {
        BatteryAlert.Critical => "Battery critical",
        BatteryAlert.Low => "Battery low",
        BatteryAlert.Full => "Fully charged",
        _ => throw new ArgumentOutOfRangeException(nameof(mostSevereAlert), mostSevereAlert, null),
    };

    private static string LineFor(DeviceAlert deviceAlert) => deviceAlert.Alert == BatteryAlert.Full
        ? $"{deviceAlert.DeviceName}: fully charged"
        : $"{deviceAlert.DeviceName}: {deviceAlert.Percent}% left";
}
