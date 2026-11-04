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

    private const int MaxTooltipLength = 63;

    private readonly IBatteryProvider _provider;
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _menu;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly int _iconSize = SystemInformation.SmallIconSize.Width;
    private Icon? _icon;
    private TrayIndicator? _indicator;
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
            Icon = SystemIcons.Application, // replaced by the first refresh
            Text = BuildTooltip(null),
            ContextMenuStrip = menu,
            Visible = true,
        };

        _timer = new System.Windows.Forms.Timer { Interval = provider is MockBatteryProvider ? 1000 : 60_000 };
        _timer.Tick += (_, _) => _ = RefreshAsync();
        _timer.Start();

        // First refresh once the message loop runs, so awaited reads resume on the UI thread.
        void FirstRefresh(object? sender, EventArgs e)
        {
            Application.Idle -= FirstRefresh;
            _ = RefreshAsync();
        }
        Application.Idle += FirstRefresh;
    }

    private async Task RefreshAsync()
    {
        if (_refreshing || _exited)
            return;

        _refreshing = true;
        string line;
        BatteryStatus? status = null;
        try
        {
            status = await _provider.ReadAsync();
            line = status.ToDisplayString();
        }
        catch (Exception)
        {
            // the provider logs the details
            line = "Battery read failed";
        }
        finally
        {
            _refreshing = false;
        }

        if (_exited)
            return;

        _notifyIcon.Text = BuildTooltip(line);

        var indicator = TrayIndicator.From(status);
        if (indicator != _indicator)
        {
            var old = _icon;
            _icon = TrayIconRenderer.Render(indicator, _iconSize);
            _notifyIcon.Icon = _icon;
            _indicator = indicator;
            old?.Dispose();
        }
    }

    private static string BuildTooltip(string? statusLine)
    {
        var version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";

        // Dev builds (prerelease part) keep "+<sha>" shortened to 7 chars; releases show only the semantic version.
        var plus = version.IndexOf('+');
        if (plus >= 0)
            version = version[..plus].Contains('-') ? version[..Math.Min(version.Length, plus + 1 + 7)] : version[..plus];

        var text = $"Logitech Battery {version}";
        if (statusLine is not null)
            text = $"{statusLine}\n{text}";
        // Safety cap: classic NotifyIcon.Text limit is 63 characters (longer throws).
        return text.Length > MaxTooltipLength ? text[..MaxTooltipLength] : text;
    }

    private void ExitApp()
    {
        _exited = true;
        _timer.Stop();
        _timer.Dispose();
        _notifyIcon.Visible = false; // hide first to avoid a ghost icon
        _notifyIcon.Dispose();
        _icon?.Dispose();
        ExitThread();
    }
}
