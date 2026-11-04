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

    // Reads after a USB change: the cable shows up at once, but the mouse can take a while to
    // reconnect wirelessly. 1 s, then every 5 s for a minute, every 10 s, 15 s and 30 s for a minute
    // each; then the regular 60 s poll. Stops as soon as the mouse is connected.
    private static readonly int[] FollowUpDelays =
    [
        1000,
        .. Enumerable.Repeat(5000, 12),
        .. Enumerable.Repeat(10_000, 6),
        .. Enumerable.Repeat(15_000, 4),
        .. Enumerable.Repeat(30_000, 2),
    ];

    private readonly IBatteryProvider _provider;
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _menu;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly System.Windows.Forms.Timer _followUp = new();
    private int _followUpStep;
    private SynchronizationContext? _ui;
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

        _followUp.Tick += async (_, _) =>
        {
            try
            {
                _followUp.Stop();
                var (ran, status) = await RefreshAsync();
                if (_exited)
                    return;
                // Skipped (another read in flight): retry the same step. Failed or not connected: next step.
                if (ran && status?.Connection == ConnectionState.Connected)
                    return;
                if (ran)
                    _followUpStep++;
                if (_followUpStep < FollowUpDelays.Length)
                {
                    _followUp.Interval = FollowUpDelays[_followUpStep];
                    _followUp.Start();
                }
            }
            catch (Exception ex)
            {
                AppLog.Write($"Follow-up read failed: {ex}"); // never escape an async void handler
            }
        };
        if (provider is HidPpBatteryProvider hid)
            hid.DevicesChanged += OnDevicesChanged;

        // First refresh once the message loop runs, so awaited reads resume on the UI thread.
        void FirstRefresh(object? sender, EventArgs e)
        {
            Application.Idle -= FirstRefresh;
            _ui = SynchronizationContext.Current;
            _ = RefreshAsync();
        }
        Application.Idle += FirstRefresh;
    }

    // Background thread: hop to the UI thread and (re)start the follow-up reads.
    private void OnDevicesChanged() => _ui?.Post(_ =>
    {
        if (_exited)
            return;
        _followUpStep = 0;
        _followUp.Stop();
        _followUp.Interval = FollowUpDelays[0];
        _followUp.Start();
    }, null);

    /// <summary>Ran is false when skipped; Status is null when skipped or the read failed.</summary>
    private async Task<(bool Ran, BatteryStatus? Status)> RefreshAsync()
    {
        if (_refreshing || _exited)
            return (false, null);

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
            return (true, status);

        try
        {
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
        catch (Exception ex)
        {
            AppLog.Write($"Tray update failed: {ex}"); // keep the previous icon
        }
        return (true, status);
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
        if (_provider is HidPpBatteryProvider hid)
            hid.DevicesChanged -= OnDevicesChanged;
        _timer.Stop();
        _timer.Dispose();
        _followUp.Stop();
        _followUp.Dispose();
        _notifyIcon.Visible = false; // hide first to avoid a ghost icon
        _notifyIcon.Dispose();
        _icon?.Dispose();
        ExitThread();
    }
}
