using LogitechBattery.Battery;

namespace LogitechBattery.Tests;

public class TrayTooltipTests
{
    private const string Version = "0.5.0";
    private static readonly IReadOnlyList<(string Name, BatteryStatus? Status)> NoOtherDevices = [];

    private static (string Name, BatteryStatus? Status) Other(string name, int percent) => (name, TestData.Status(percent));

    private static (string Name, BatteryStatus? Status) Other(string name, ConnectionState connection) =>
        (name, TestData.Status(null, ChargingState.Unknown, connection));

    [Fact]
    public void The_tooltip_has_device_status_estimate_and_app_name_on_separate_lines() =>
        Assert.Equal(
            "PRO X2: 85% · Discharging\n96 h left\nLogitech Battery 0.5.0",
            TrayTooltip.Build("PRO X2", "85% · Discharging", "96 h left", NoOtherDevices, Version));

    [Fact]
    public void A_missing_estimate_is_left_out() =>
        Assert.Equal("PRO X2: Asleep\nLogitech Battery 0.5.0", TrayTooltip.Build("PRO X2", "Asleep", null, NoOtherDevices, Version));

    [Fact]
    public void A_missing_status_and_estimate_leave_only_the_app_name() =>
        Assert.Equal("Logitech Battery 0.5.0", TrayTooltip.Build("PRO X2", null, null, NoOtherDevices, Version));

    [Fact]
    public void One_other_device_gets_its_own_line_before_the_app_name() =>
        Assert.Equal(
            "PRO X2: 85%\nG915: 40%\nLogitech Battery 0.5.0",
            TrayTooltip.Build("PRO X2", "85%", null, [Other("G915", 40)], Version));

    [Fact]
    public void Four_other_devices_are_joined_on_one_line_with_their_percent_or_state() =>
        Assert.Equal(
            "PRO X2: 85%\n96 h left\nG915: 40% · M720: Asleep · K380: Not found · MX Keys: Read failed\nLogitech Battery 0.5.0",
            TrayTooltip.Build(
                "PRO X2", "85%", "96 h left",
                [Other("G915", 40), Other("M720", ConnectionState.Asleep), Other("K380", ConnectionState.NotFound), ("MX Keys", null)],
                Version));

    [Fact]
    public void A_tooltip_of_the_maximum_length_is_unchanged()
    {
        var statusLine = new string('s', TrayTooltip.MaxLength - "PRO X2: ".Length - "\nLogitech Battery v".Length);

        var tooltip = TrayTooltip.Build("PRO X2", statusLine, null, NoOtherDevices, "v");

        Assert.Equal(TrayTooltip.MaxLength, tooltip.Length);
        Assert.EndsWith("Logitech Battery v", tooltip);
    }

    [Fact]
    public void A_longer_tooltip_is_cut_to_the_maximum_length() =>
        Assert.Equal(TrayTooltip.MaxLength, TrayTooltip.Build("PRO X2", new string('s', 200), null, NoOtherDevices, Version).Length);

    [Fact]
    public void A_cut_tooltip_drops_the_app_name_before_the_other_devices()
    {
        var longNames = new[] { Other(new string('a', 32), 10), Other(new string('b', 32), 20), Other(new string('c', 32), 30) };

        var tooltip = TrayTooltip.Build("PRO X2", "85%", null, longNames, Version);

        Assert.Equal(TrayTooltip.MaxLength, tooltip.Length);
        Assert.DoesNotContain("Logitech", tooltip);
        Assert.Contains($"{new string('a', 32)}: 10% · ", tooltip);
    }
}
