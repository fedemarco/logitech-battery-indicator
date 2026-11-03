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

    private const int MaxTooltipLength = 127;

    private readonly IBatteryProvider _provider;
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _menu;
    private readonly System.Windows.Forms.Timer _timer;
    private bool _refreshing;
    private bool _exited;

    public TrayApplicationContext(IBatteryProvider provider)
    {
        _provider = provider;

        var menu = _menu = new ContextMenuStrip();

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

        // 1 s is for the mock; the real refresh interval comes in milestone 6.
        _timer = new System.Windows.Forms.Timer { Interval = 1000 };
        _timer.Tick += (_, _) => _ = RefreshAsync();
        _timer.Start();

        _ = RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        if (_refreshing || _exited)
            return;

        _refreshing = true;
        string line;
        try
        {
            line = (await _provider.ReadAsync()).ToDisplayString();
        }
        catch (Exception ex)
        {
            line = $"Battery read failed: {ex.Message}";
        }
        finally
        {
            _refreshing = false;
        }

        if (!_exited)
            _notifyIcon.Text = BuildTooltip(line);
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
        // NotifyIcon.Text throws at 128+ characters (limit is 127 since .NET Core 3.0).
        return text.Length > MaxTooltipLength ? text[..MaxTooltipLength] : text;
    }

    private void ExitApp()
    {
        _exited = true;
        _timer.Stop();
        _timer.Dispose();
        _notifyIcon.Visible = false; // hide first to avoid a ghost icon
        _notifyIcon.Dispose();
        _menu.Dispose();
        ExitThread();
    }
}
