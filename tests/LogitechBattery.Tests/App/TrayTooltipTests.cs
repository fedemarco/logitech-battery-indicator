namespace LogitechBattery.Tests;

public class TrayTooltipTests
{
    [Fact]
    public void The_tooltip_has_status_estimate_and_app_name_on_separate_lines() =>
        Assert.Equal("85% · Discharging\n96 h left\nLogitech Battery 0.5.0", TrayTooltip.Build("85% · Discharging", "96 h left", "0.5.0"));

    [Fact]
    public void A_missing_estimate_is_left_out() =>
        Assert.Equal("Mouse asleep\nLogitech Battery 0.5.0", TrayTooltip.Build("Mouse asleep", null, "0.5.0"));

    [Fact]
    public void A_missing_status_and_estimate_leave_only_the_app_name() =>
        Assert.Equal("Logitech Battery 0.5.0", TrayTooltip.Build(null, null, "0.5.0"));

    [Fact]
    public void A_tooltip_of_the_maximum_length_is_unchanged()
    {
        var statusLine = new string('s', TrayTooltip.MaxLength - "\nLogitech Battery v".Length);

        var tooltip = TrayTooltip.Build(statusLine, null, "v");

        Assert.Equal(TrayTooltip.MaxLength, tooltip.Length);
        Assert.EndsWith("Logitech Battery v", tooltip);
    }

    [Fact]
    public void A_longer_tooltip_is_cut_to_the_maximum_length() =>
        Assert.Equal(TrayTooltip.MaxLength, TrayTooltip.Build(new string('s', 200), null, "0.5.0").Length);
}
