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
    private BatteryHistory? _history; // of the tray device: the drain rate and the overlay follow it
    private readonly SettingsFile _settingsFile;
    private AppSettings _settings;
    private readonly DeviceTracker _tracker;
    private readonly bool _isSimulator;
    private readonly FollowUpSchedule _followUpSchedule = new();
    private readonly StuckReadWatch _stuckReadWatch = new(StuckReadAfter);

    private readonly NotifyIcon _notifyIcon;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly System.Windows.Forms.Timer _followUp = new();
    private volatile SynchronizationContext? _ui;

    private Icon? _icon;
    private int _iconSize;
    private TrayIndicator? _indicator;
    private ShowInTrayMenu? _showInTrayMenu;
    private OverlayForm? _overlay;
    private long? _overlayClosedAtMs;
    private IReadOnlyList<DeviceReading> _readings = [];
    private TrayDevices? _devices; // null until the first read
    private double? _drainPercentPerDay;
    private bool _refreshing;
    private bool _exited;

    public TrayApplicationContext(IBatteryProvider provider, DeviceHistories? histories, SettingsFile settingsFile)
    {
        _provider = provider;
        _histories = histories;
        _settingsFile = settingsFile;
        _settings = settingsFile.Load();
        _tracker = new DeviceTracker(_settings.DeviceNames);
        if (SynchronizationContext.Current is WindowsFormsSynchronizationContext ui)
            _ui = ui; // otherwise captured on the first idle

        _isSimulator = provider is MockBatteryProvider;
        if (!_isSimulator)
            AutoStart.EnableOnFirstRun();

        _notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application, // replaced by the first refresh
            Text = TrayTooltip.Build(DeviceInfo.NoDeviceYet.Name, null, null, [], _displayVersion),
            ContextMenuStrip = BuildMenu(),
            Visible = true,
        };

        _notifyIcon.MouseClick += OnTrayClick;

        _timer = StartPollTimer(_isSimulator ? MockPollIntervalMs : PollIntervalMs);
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
        _showInTrayMenu = new ShowInTrayMenu(ChooseTrayDevice);
        if (_provider is MockBatteryProvider mock)
        {
            menu.Items.Add(BuildSimulatorMenu(mock));
            menu.Items.Add(_showInTrayMenu.Item);
            menu.Items.Add(NotificationsMenu.Build(() => _settings.Alerts, ApplyAlertSettings));
            menu.Items.Add(new ToolStripSeparator());
        }
        else
        {
            menu.Items.Add(_showInTrayMenu.Item);
            menu.Items.Add(NotificationsMenu.Build(() => _settings.Alerts, ApplyAlertSettings));
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

    private void ApplyAlertSettings(AlertSettings updated)
    {
        _settings = _settings with { Alerts = updated }; // applies for this session even when the save fails
        if (_settingsFile.Save(_settings) is { } problem)
            ShowWarning(problem);
        _ = RefreshAsync(); // a new level is checked against the current battery at once
    }

    private void ChooseTrayDevice(DeviceId device)
    {
        _settings = _settings with { TrayDevice = device }; // applies for this session even when the save fails
        if (!_isSimulator && _settingsFile.Save(_settings) is { } problem)
            ShowWarning(problem);
        ShowTrayDevice(historyChanged: false); // the last readings are enough, no new read
    }

    private void SaveDeviceNames(IReadOnlyDictionary<DeviceId, string> names)
    {
        _settings = _settings with { DeviceNames = names };
        if (_settingsFile.Save(_settings) is { } problem)
            AppLog.Write(problem); // automatic save: no dialog
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
        IReadOnlyList<DeviceReading> readings = [];
        try
        {
            readings = await _provider.ReadAsync();
        }
        catch (Exception)
        {
            // the provider logs the details
        }
        finally
        {
            _refreshing = false;
            _stuckReadWatch.ReadFinished();
        }

        if (_exited) // the tray icon is gone if the app exited during the read
            return (true, null);

        _readings = readings;
        ShowTrayDevice(historyChanged: RecordReadings(CurrentDevices().Tray.Device));
        NotifyAlerts();
        return (true, _devices?.Tray.Status);
    }

    private TrayDevices CurrentDevices() =>
        TrayDevices.From(_readings, _settings.TrayDevice, _settings.DeviceNames, DateTimeOffset.UtcNow);

    // Returns true when the tray device's history got a new line.
    private bool RecordReadings(DeviceInfo trayDevice)
    {
        if (_histories is null)
            return false;

        var now = DateTimeOffset.UtcNow;
        var changed = false;
        foreach (var (device, status) in _readings)
        {
            if (status is null || device.Id == DeviceInfo.NoDeviceYet.Id)
                continue;
            var wrote = _histories.For(device, now).Record(status);
            changed |= wrote && device.Id == trayDevice.Id;
        }
        return changed;
    }

    // The tray device's history is null in the simulator and while no device is known.
    private bool UseHistoryOf(DeviceInfo trayDevice)
    {
        var history = _histories is null || trayDevice.Id == DeviceInfo.NoDeviceYet.Id
            ? null
            : _histories.For(trayDevice, DateTimeOffset.UtcNow);
        var switched = history != _history;
        _history = history;
        return switched;
    }

    // Everything that follows the tray device: history, drain rate, overlay, tooltip, icon and menu.
    private void ShowTrayDevice(bool historyChanged)
    {
        try
        {
            var devices = CurrentDevices();
            _devices = devices;
            var historyShown = UseHistoryOf(devices.Tray.Device) || historyChanged;
            if (historyShown)
                RecomputeDrainRate();
            _showInTrayMenu?.Update(devices.Items);
            ShowInOverlay(devices, historyShown);
            RefreshTooltip();
            UpdateIcon(devices.Tray.Status);
        }
        catch (Exception ex)
        {
            AppLog.Write($"Tray update failed: {ex}"); // keep the previous icon
        }
    }

    private void ShowInOverlay(TrayDevices devices, bool historyShown)
    {
        if (_overlay is null)
            return;
        if (historyShown)
            _overlay.ShowHistory(_history);
        _overlay.SetStatus(devices.Tray.Status, devices.TrayName, devices.StatusLine, _drainPercentPerDay);
    }

    private void UpdateIcon(BatteryStatus? status)
    {
        var indicator = TrayIndicator.From(status);
        var iconSize = TrayIconSize.Current(); // checked each time: the scaling can change while the app runs
        if (indicator != _indicator || iconSize != _iconSize)
            ReplaceIcon(indicator, iconSize);
    }

    private void NotifyAlerts()
    {
        try
        {
            var update = _tracker.Update(_readings, _settings.Alerts);
            foreach (var alert in update.Alerts)
                ShowAlert(alert);
            if (update.NamesToSave is { } names && !_isSimulator)
                SaveDeviceNames(names);
        }
        catch (Exception ex)
        {
            AppLog.Write($"Notifications failed: {ex}");
        }
    }

    private void RefreshTooltip()
    {
        if (_devices is not { } devices)
            return;

        var remainingText = devices.Tray.Status is null ? null : RemainingTime.RemainingText(devices.Tray.Status, _drainPercentPerDay);
        _notifyIcon.Text = TrayTooltip.Build(devices.TrayName, devices.StatusLine, remainingText, devices.Others, _displayVersion);
    }

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

        var overlay = new OverlayForm(_history, _isSimulator);
        if (_devices is { } devices)
            overlay.SetStatus(devices.Tray.Status, devices.TrayName, devices.StatusLine, _drainPercentPerDay);
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

    private void ShowAlert(DeviceAlert deviceAlert)
    {
        var (title, text) = BatteryAlertText.For(deviceAlert.Alert, deviceAlert.Percent, deviceAlert.DeviceName);
        var icon = deviceAlert.Alert == BatteryAlert.Full ? ToolTipIcon.Info : ToolTipIcon.Warning;
        AppLog.Write($"Notification: {title} ({deviceAlert.Percent}%)");
        _notifyIcon.ShowBalloonTip(BalloonTimeoutMs, title, text, icon);
    }

    private void RecomputeDrainRate()
    {
        if (_history is null)
        {
            _drainPercentPerDay = null;
            return;
        }

        var now = DateTimeOffset.UtcNow;
        _drainPercentPerDay = RemainingTime.DrainPercentPerDay(_history.ReadSince(now - RemainingTime.Window), now);
    }

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
