using LogitechBattery.Battery;

namespace LogitechBattery.Tests;

/// <summary>The tray icon is looked up by name at runtime; a typo or a renamed file would only show on Windows.</summary>
public class TrayIconAssetTests
{
    [Fact]
    public void Every_tray_indicator_has_an_embedded_icon_with_the_exact_name()
    {
        var assetNames = new HashSet<string>(
            Directory.GetFiles(AssetsFolder(), "*.png").Select(Path.GetFileNameWithoutExtension)!,
            StringComparer.Ordinal);

        Assert.All(AllIndicatorNames(), name => Assert.Contains(name, assetNames));
    }

    private static IEnumerable<string> AllIndicatorNames()
    {
        var connections = Enum.GetValues<ConnectionState>();
        var chargingStates = Enum.GetValues<ChargingState>();
        var percents = Enumerable.Range(0, BatteryStatus.FullPercent + 1).Select(percent => (int?)percent).Append(null);

        var statuses = from connection in connections
                       from charging in chargingStates
                       from percent in percents
                       select TestData.Status(percent, charging, connection);

        return statuses.Select(status => TrayIndicator.From(status).Icon)
            .Append(TrayIndicator.From(null).Icon)
            .Distinct();
    }

    private static string AssetsFolder()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "LogitechBattery.slnx")))
            folder = folder.Parent;
        return Path.Combine(folder?.FullName ?? throw new DirectoryNotFoundException("LogitechBattery.slnx not found above the test folder."),
            "src", "LogitechBattery.App", "Assets");
    }
}
