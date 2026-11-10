using System.Reflection;
using Microsoft.Win32;
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

    private const long ReopenGuardMs = 300; // focus loss closes the panel just before the tray click that caused it arrives
    private const int MockPollIntervalMs = 1000;
    private const int PollIntervalMs = 60_000;
    private const int BalloonTimeoutMs = 10_000;
    private const string AppName = "Logitech Battery";
    private static readonly TimeSpan StuckReadAfter = TimeSpan.FromSeconds(60);

    private readonly string _displayVersion = AppVersion.Display(AppVersion.InformationalVersionOf(Assembly.GetExecutingAssembly()));
    private readonly IBatteryProvider _provider;
    private readonly DeviceHistories? _histories;
    private BatteryHistory? _history; // of the first device: the drain rate, the overlay and the tooltip follow it
    private readonly SettingsFile _settingsFile;
    private AlertSettings _settings;
    private readonly BatteryAlerts _alerts = new();
    private readonly FollowUpSchedule _followUpSchedule = new();
    private readonly StuckReadWatch _stuckReadWatch = new(StuckReadAfter);

    private readonly NotifyIcon _notifyIcon;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly System.Windows.Forms.Timer _followUp = new();
    private volatile SynchronizationContext? _ui;

    private Icon? _icon;
    private int _iconSize;
    private TrayIndicator? _indicator;
    private OverlayForm? _overlay;
    private long? _overlayClosedAtMs;
    private BatteryStatus? _status;
    private string _statusLine = "";
    private double? _drainPercentPerDay;
    private bool _refreshing;
    private bool _exited;

    public TrayApplicationContext(IBatteryProvider provider, DeviceHistories? histories, SettingsFile settingsFile)
    {
        _provider = provider;
        _histories = histories;
        _settingsFile = settingsFile;
        _settings = settingsFile.Load();
        if (SynchronizationContext.Current is WindowsFormsSynchronizationContext ui)
            _ui = ui; // otherwise captured on the first idle

        var isSimulator = provider is MockBatteryProvider;
        if (!isSimulator)
            AutoStart.EnableOnFirstRun();

        _notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application, // replaced by the first refresh
            Text = BuildTooltip(null, null),
            ContextMenuStrip = BuildMenu(),
            Visible = true,
        };

        _notifyIcon.MouseClick += OnTrayClick;

        _timer = StartPollTimer(isSimulator ? MockPollIntervalMs : PollIntervalMs);
        _followUp.Tick += OnFollowUpTickAsync;
        if (provider is HidPpBatteryProvider hid)
        {
            hid.ChangeDetected += OnChangeDetected;
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
        }

        Application.Idle += OnFirstIdle;
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        if (_provider is MockBatteryProvider mock)
        {
            menu.Items.Add(BuildSimulatorMenu(mock));
            menu.Items.Add(NotificationsMenu.Build(() => _settings, ApplySettings));
            menu.Items.Add(new ToolStripSeparator());
        }
        else
        {
            menu.Items.Add(NotificationsMenu.Build(() => _settings, ApplySettings));
            menu.Items.Add(new ToolStripSeparator());
            var startItem = BuildStartupItem();
            menu.Opening += (_, _) => startItem.Checked = AutoStart.IsEnabled();
            menu.Items.Add(startItem);
            if (_histories is not null)
                menu.Items.Add(BuildClearHistoryItem(_histories));
        }
        menu.Items.Add("Exit", null, (_, _) => ExitApp());
        return menu;
    }

    private ToolStripMenuItem BuildSimulatorMenu(MockBatteryProvider mock)
    {
        var simulator = new ToolStripMenuItem("Simulator");
        var modeItems = new List<ToolStripMenuItem>();
        foreach (var (label, mode) in SimulatorItems)
        {
            var item = new ToolStripMenuItem(label) { Checked = mock.Mode == mode, Tag = mode };
            item.Click += (_, _) =>
            {
                mock.Mode = mode;
                foreach (var other in modeItems)
                    other.Checked = (MockMode)other.Tag! == mode;
                _ = RefreshAsync();
            };
            modeItems.Add(item);
            simulator.DropDownItems.Add(item);
        }
        simulator.DropDownItems.Add(new ToolStripSeparator());
        simulator.DropDownItems.Add(BuildSeveralDevicesItem(mock));
        return simulator;
    }

    private ToolStripMenuItem BuildSeveralDevicesItem(MockBatteryProvider mock)
    {
        var item = new ToolStripMenuItem("Several devices") { Checked = mock.SeveralDevices };
        item.Click += (_, _) =>
        {
            mock.SeveralDevices = !mock.SeveralDevices;
            item.Checked = mock.SeveralDevices;
            _ = RefreshAsync();
        };
        return item;
    }

    private static ToolStripMenuItem BuildStartupItem()
    {
        var startItem = new ToolStripMenuItem("Start with Windows");
        startItem.Click += (_, _) => ToggleStartWithWindows();
        return startItem;
    }

    private static void ToggleStartWithWindows()
    {
        if (AutoStart.IsEnabled())
        {
            if (!AutoStart.Disable())
                ShowWarning("Couldn't turn off Start with Windows. The reason is in log.txt.");
            return;
        }

        if (!AutoStart.Enable()) // also rewrites a stale path and clears a Task Manager "disabled"
            ShowWarning("Couldn't turn on Start with Windows. The reason is in log.txt.");
    }

    private ToolStripMenuItem BuildClearHistoryItem(DeviceHistories histories)
    {
        var clearItem = new ToolStripMenuItem("Clear history…");
        clearItem.Click += (_, _) =>
        {
            var answer = MessageBox.Show(
                "Delete all battery history? This cannot be undone.",
                AppName,
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes)
                return;

            var cleared = histories.ClearAll();
            if (cleared)
            {
                _drainPercentPerDay = null;
                RefreshTooltip();
            }
            else
                ShowWarning("Couldn't delete the battery history. Close any program that has a history file open and try again.");
        };
        return clearItem;
    }

    private void ApplySettings(AlertSettings updated)
    {
        _settings = updated; // applies for this session even when the save fails
        if (_settingsFile.Save(updated) is { } problem)
            ShowWarning(problem);
        _ = RefreshAsync(); // a new level is checked against the current battery at once
    }

    private static void ShowWarning(string message) =>
        MessageBox.Show(message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);

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

    // Raised on a system thread; it only sets a flag, writes the log and posts to the UI thread, like OnChangeDetected.
    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        switch (e.Mode)
        {
            case PowerModes.Suspend:
                AppLog.Write("PC suspending");
                break;
            case PowerModes.Resume:
                AppLog.Write("PC resumed");
                (_provider as HidPpBatteryProvider)?.ReopenDevices();
                OnChangeDetected(); // reads again after 1 s, then follows up like after any change
                break;
        }
    }

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
        {
            if (_refreshing && _stuckReadWatch.ShouldReport(Environment.TickCount64))
                AppLog.Write($"Battery read still running after {StuckReadAfter.TotalSeconds:0} s");
            return (false, null);
        }

        _refreshing = true;
        _stuckReadWatch.ReadStarted(Environment.TickCount64);
        string statusLine;
        BatteryStatus? status = null;
        try
        {
            var readings = await _provider.ReadAsync();
            status = readings[0].Status ?? throw new InvalidOperationException("The device reading failed.");
            statusLine = status.ToDisplayString();
            if (RecordReadings(readings))
            {
                RecomputeDrainRate();
                if (!_exited)
                    _overlay?.ReloadHistory();
            }
        }
        catch (Exception)
        {
            // the provider logs the details
            statusLine = "Battery read failed";
        }
        finally
        {
            _refreshing = false;
            _stuckReadWatch.ReadFinished();
        }

        if (!_exited) // the tray icon is gone if the app exited during the read
            UpdateTray(status, statusLine);
        return (true, status);
    }

    // Returns true when the first device's history changed: a new line, or it is the first read of this session.
    private bool RecordReadings(IReadOnlyList<DeviceReading> readings)
    {
        if (_histories is null)
            return false;

        var now = DateTimeOffset.UtcNow;
        var firstDevice = readings[0].Device;
        var firstHistory = _histories.For(firstDevice, now);
        var changed = firstHistory != _history;
        _history = firstHistory;
        foreach (var (device, status) in readings)
        {
            if (status is null)
                continue;
            var wrote = _histories.For(device, now).Record(status);
            changed |= wrote && device == firstDevice;
        }
        return changed;
    }

    private void UpdateTray(BatteryStatus? status, string statusLine)
    {
        _status = status;
        _statusLine = statusLine;
        try
        {
            _overlay?.SetStatus(status, statusLine, _drainPercentPerDay);
            RefreshTooltip();

            var indicator = TrayIndicator.From(status);
            var iconSize = TrayIconSize.Current(); // checked each time: the scaling can change while the app runs
            if (indicator != _indicator || iconSize != _iconSize)
                ReplaceIcon(indicator, iconSize);

            if (status is not null && _alerts.Update(status, _settings) is { } alert)
                ShowAlert(alert, status.Percent);
        }
        catch (Exception ex)
        {
            AppLog.Write($"Tray update failed: {ex}"); // keep the previous icon
        }
    }

    private void RefreshTooltip() =>
        _notifyIcon.Text = BuildTooltip(_statusLine, _status is null ? null : RemainingTime.RemainingText(_status, _drainPercentPerDay));

    private bool OverlayJustClosed() =>
        _overlayClosedAtMs is { } closedAt && Environment.TickCount64 - closedAt < ReopenGuardMs;

    private void OnTrayClick(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
            return;
        if (_overlay is not null)
        {
            _overlay.Close();
            return;
        }
        if (OverlayJustClosed())
            return;

        var overlay = new OverlayForm(_history);
        overlay.SetStatus(_status, _statusLine, _drainPercentPerDay);
        overlay.FormClosed += (_, _) =>
        {
            _overlay = null;
            _overlayClosedAtMs = Environment.TickCount64;
        };
        overlay.Show();
        _overlay = overlay; // after Show, so a failing Show leaves no half-open panel behind
    }

    private void ReplaceIcon(TrayIndicator indicator, int iconSize)
    {
        var oldIcon = _icon;
        _icon = TrayIconRenderer.Render(indicator, iconSize);
        _notifyIcon.Icon = _icon;
        _indicator = indicator;
        _iconSize = iconSize;
        oldIcon?.Dispose();
    }

    private void ShowAlert(BatteryAlert alert, int? percent)
    {
        var (title, text) = BatteryAlertText.For(alert, percent);
        var icon = alert == BatteryAlert.Full ? ToolTipIcon.Info : ToolTipIcon.Warning;
        AppLog.Write($"Notification: {title} ({percent}%)");
        _notifyIcon.ShowBalloonTip(BalloonTimeoutMs, title, text, icon);
    }

    private void RecomputeDrainRate()
    {
        if (_history is null)
            return;

        var now = DateTimeOffset.UtcNow;
        _drainPercentPerDay = RemainingTime.DrainPercentPerDay(_history.ReadSince(now - RemainingTime.Window), now);
    }

    private string BuildTooltip(string? statusLine, string? remainingText) =>
        TrayTooltip.Build(statusLine, remainingText, _displayVersion);

    private void ExitApp()
    {
        _exited = true;
        if (_provider is HidPpBatteryProvider hid)
        {
            hid.ChangeDetected -= OnChangeDetected;
            SystemEvents.PowerModeChanged -= OnPowerModeChanged; // static event: it would keep this object alive and run after exit
        }
        _overlay?.Dispose();
        _overlay = null;
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
