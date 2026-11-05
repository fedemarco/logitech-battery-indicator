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

    private const int MockPollIntervalMs = 1000;
    private const int PollIntervalMs = 60_000;
    private const int BalloonTimeoutMs = 10_000;
    private const int MaxTooltipLength = 63;
    private const int ShortShaLength = 7;

    private readonly IBatteryProvider _provider;
    private readonly BatteryAlerts _alerts = new();
    private readonly BatteryHistory? _history;
    private readonly NotifyIcon _notifyIcon;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly System.Windows.Forms.Timer _followUp = new();
    private readonly FollowUpSchedule _followUpSchedule = new();
    private volatile SynchronizationContext? _ui;
    private readonly int _iconSize = SystemInformation.SmallIconSize.Width;
    private Icon? _icon;
    private TrayIndicator? _indicator;
    private bool _refreshing;
    private bool _exited;

    public TrayApplicationContext(IBatteryProvider provider, BatteryHistory? history)
    {
        _provider = provider;
        _history = history;
        if (SynchronizationContext.Current is WindowsFormsSynchronizationContext ui)
            _ui = ui; // otherwise captured on the first idle

        var menu = new ContextMenuStrip();
        int pollIntervalMs;
        if (provider is MockBatteryProvider mock)
        {
            menu.Items.Add(BuildSimulatorMenu(mock));
            menu.Items.Add(new ToolStripSeparator());
            pollIntervalMs = MockPollIntervalMs;
        }
        else
        {
            AutoStart.EnableOnFirstRun();
            var startItem = BuildStartupItem();
            menu.Opening += (_, _) => startItem.Checked = AutoStart.IsEnabled();
            menu.Items.Add(startItem);
            if (history is not null)
                menu.Items.Add(BuildClearHistoryItem(history));
            pollIntervalMs = PollIntervalMs;
        }
        menu.Items.Add("Exit", null, (_, _) => ExitApp());

        _notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application, // replaced by the first refresh
            Text = BuildTooltip(null),
            ContextMenuStrip = menu,
            Visible = true,
        };

        _timer = StartPollTimer(pollIntervalMs);
        _followUp.Tick += OnFollowUpTickAsync;
        if (provider is HidPpBatteryProvider hid)
            hid.ChangeDetected += OnChangeDetected;

        Application.Idle += OnFirstIdle;
    }

    private ToolStripMenuItem BuildSimulatorMenu(MockBatteryProvider mock)
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
        return simulator;
    }

    private static ToolStripMenuItem BuildStartupItem()
    {
        var startItem = new ToolStripMenuItem("Start with Windows");
        startItem.Click += (_, _) =>
        {
            if (AutoStart.IsEnabled())
                AutoStart.Disable();
            else
                AutoStart.Enable(); // also rewrites a stale path and clears a Task Manager "disabled"
        };
        return startItem;
    }

    private static ToolStripMenuItem BuildClearHistoryItem(BatteryHistory history)
    {
        var clearItem = new ToolStripMenuItem("Clear history…");
        clearItem.Click += (_, _) =>
        {
            var answer = MessageBox.Show(
                "Delete all battery history? This cannot be undone.",
                "Logitech Battery",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (answer == DialogResult.Yes && !history.Clear())
                MessageBox.Show(
                    "Couldn't delete the battery history. Close any program that has history.csv open and try again.",
                    "Logitech Battery",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
        };
        return clearItem;
    }

    private System.Windows.Forms.Timer StartPollTimer(int intervalMs)
    {
        var timer = new System.Windows.Forms.Timer { Interval = intervalMs };
        timer.Tick += (_, _) => _ = RefreshAsync();
        timer.Start();
        return timer;
    }

    // First refresh once the message loop runs, so awaited reads resume on the UI thread.
    private void OnFirstIdle(object? sender, EventArgs e)
    {
        Application.Idle -= OnFirstIdle;
        _ui ??= SynchronizationContext.Current;
        _ = RefreshAsync();
    }

    // Background thread: hop to the UI thread and restart the follow-up reads.
    private void OnChangeDetected() => _ui?.Post(_ =>
    {
        if (_exited)
            return;
        StartFollowUpTimer(_followUpSchedule.Restart());
    }, null);

    private async void OnFollowUpTickAsync(object? sender, EventArgs e)
    {
        try
        {
            _followUp.Stop();
            var generation = _followUpSchedule.Generation;
            var (ran, status) = await RefreshAsync();
            if (_exited)
                return;

            if (_followUpSchedule.Finish(generation, ran, status) is { } delayMs)
                StartFollowUpTimer(delayMs);
        }
        catch (Exception ex)
        {
            AppLog.Write($"Follow-up read failed: {ex}"); // never escape an async void handler
        }
    }

    private void StartFollowUpTimer(int delayMs)
    {
        _followUp.Stop();
        _followUp.Interval = delayMs;
        _followUp.Start();
    }

    /// <summary>Ran is false when skipped; Status is null when skipped or the read failed.</summary>
    private async Task<(bool Ran, BatteryStatus? Status)> RefreshAsync()
    {
        if (_refreshing || _exited)
            return (false, null);

        _refreshing = true;
        string statusLine;
        BatteryStatus? status = null;
        try
        {
            status = await _provider.ReadAsync();
            statusLine = status.ToDisplayString();
            _history?.Record(status);
        }
        catch (Exception)
        {
            // the provider logs the details
            statusLine = "Battery read failed";
        }
        finally
        {
            _refreshing = false;
        }

        if (!_exited) // the tray icon is gone if the app exited during the read
            UpdateTray(status, statusLine);
        return (true, status);
    }

    private void UpdateTray(BatteryStatus? status, string statusLine)
    {
        try
        {
            _notifyIcon.Text = BuildTooltip(statusLine);

            var indicator = TrayIndicator.From(status);
            if (indicator != _indicator)
                ReplaceIcon(indicator);

            if (status is not null && _alerts.Update(status) is { } alert)
                ShowAlert(alert, status.Percent);
        }
        catch (Exception ex)
        {
            AppLog.Write($"Tray update failed: {ex}"); // keep the previous icon
        }
    }

    private void ReplaceIcon(TrayIndicator indicator)
    {
        var oldIcon = _icon;
        _icon = TrayIconRenderer.Render(indicator, _iconSize);
        _notifyIcon.Icon = _icon;
        _indicator = indicator;
        oldIcon?.Dispose();
    }

    private void ShowAlert(BatteryAlert alert, int? percent)
    {
        var (title, text, icon) = alert switch
        {
            BatteryAlert.Critical => ("Mouse battery critical", $"{percent}% left.", ToolTipIcon.Warning),
            BatteryAlert.Low => ("Mouse battery low", $"{percent}% left. Charge it soon.", ToolTipIcon.Warning),
            BatteryAlert.Full => ("Mouse fully charged", $"{percent}%.", ToolTipIcon.Info),
            _ => throw new ArgumentOutOfRangeException(nameof(alert), alert, null),
        };
        AppLog.Write($"Notification: {title} ({percent}%)");
        _notifyIcon.ShowBalloonTip(BalloonTimeoutMs, title, text, icon);
    }

    private static string BuildTooltip(string? statusLine)
    {
        var version = DisplayVersion(AppVersion.InformationalVersionOf(Assembly.GetExecutingAssembly()));
        var text = statusLine is null ? $"Logitech Battery {version}" : $"{statusLine}\nLogitech Battery {version}";
        // Safety cap: classic NotifyIcon.Text limit is 63 characters (longer throws).
        return text.Length > MaxTooltipLength ? text[..MaxTooltipLength] : text;
    }

    // Releases show the semantic version only; dev builds (prerelease part) also keep a short commit so builds can be told apart.
    private static string DisplayVersion(string? informationalVersion)
    {
        if (informationalVersion is null)
            return "unknown";

        var plus = informationalVersion.IndexOf('+');
        if (plus < 0)
            return informationalVersion;

        var isDevBuild = informationalVersion[..plus].Contains('-');
        return isDevBuild
            ? informationalVersion[..Math.Min(informationalVersion.Length, plus + 1 + ShortShaLength)]
            : informationalVersion[..plus];
    }

    private void ExitApp()
    {
        _exited = true;
        if (_provider is HidPpBatteryProvider hid)
            hid.ChangeDetected -= OnChangeDetected;
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
