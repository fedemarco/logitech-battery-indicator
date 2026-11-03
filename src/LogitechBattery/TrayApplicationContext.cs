using System.Reflection;
using LogitechBattery.Battery;

namespace LogitechBattery;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private static readonly (string Label, MockMode Mode)[] SimulatorItems =
    [
        ("Auto cycle", MockMode.Auto),
        ("85% discharging", MockMode.Discharging85),
        ("25% discharging", MockMode.Discharging25),
        ("10% discharging", MockMode.Discharging10),
        ("60% charging", MockMode.Charging60),
        ("100% full", MockMode.Full100),
        ("Asleep", MockMode.Asleep),
        ("Not found", MockMode.NotFound),
    ];

    private readonly IBatteryProvider _provider;
    private readonly NotifyIcon _notifyIcon;

    public TrayApplicationContext(IBatteryProvider provider)
    {
        _provider = provider;

        var menu = new ContextMenuStrip();

        if (provider is MockBatteryProvider mock)
        {
            var simulator = new ToolStripMenuItem("Simulator");
            foreach (var (label, mode) in SimulatorItems)
            {
                var item = new ToolStripMenuItem(label) { Checked = mock.Mode == mode, Tag = mode };
                item.Click += (_, _) =>
                {
                    mock.Mode = mode;
                    foreach (ToolStripMenuItem other in simulator.DropDownItems)
                        other.Checked = (MockMode)other.Tag! == mode;
                    _ = RefreshAsync();
                };
                simulator.DropDownItems.Add(item);
            }
            menu.Items.Add(simulator);
            menu.Items.Add(new ToolStripSeparator());
        }

        menu.Items.Add("Exit", null, (_, _) => ExitApp());

        _notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = BuildTooltip(null),
            ContextMenuStrip = menu,
            Visible = true,
        };

        _ = RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        var status = await _provider.ReadAsync();
        _notifyIcon.Text = BuildTooltip(status.ToDisplayString());
    }

    private static string BuildTooltip(string? statusLine)
    {
        var version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";

        // Shorten "+<sha>" build metadata to 7 characters.
        var plus = version.IndexOf('+');
        if (plus >= 0 && version.Length - plus - 1 > 7)
            version = version[..(plus + 1 + 7)];

        var text = $"Logitech Battery {version}";
        if (statusLine is not null)
            text = $"{statusLine}\n{text}";
        return text.Length > 63 ? text[..63] : text;
    }

    private void ExitApp()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        ExitThread();
    }
}
