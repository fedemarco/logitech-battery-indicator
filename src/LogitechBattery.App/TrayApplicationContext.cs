using System.Reflection;
using LogitechBattery.Battery;

namespace LogitechBattery;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private const long ReopenGuardMs = 300; // focus loss closes the panel just before the tray click that caused it arrives
    internal const string AppName = "Logitech Battery";
    private static readonly TimeSpan StuckReadAfter = TimeSpan.FromSeconds(60);

    private readonly string? _informationalVersion = AppVersion.InformationalVersionOf(Assembly.GetExecutingAssembly());
    private readonly string _displayVersion;
    private readonly IBatteryProvider _provider;
    private readonly DeviceHistories? _histories;
    private BatteryHistory? _history; // of the tray device: the drain rate and the overlay follow it
    private readonly SettingsFile _settingsFile;
    private AppSettings _settings;
    private DeviceId? _mockTrayDevice; // the simulator's choice lives here so that no save can write it to settings.ini
    private readonly DeviceTracker _tracker;
    private readonly bool _isSimulator;
    private readonly StuckReadWatch _stuckReadWatch = new(StuckReadAfter);

    private readonly TrayPresenter _presenter;
    private readonly RefreshScheduler _scheduler;
    private readonly UpdateNotifier _updateNotifier;
    private readonly ShowInTrayMenu _showInTrayMenu;
    private OverlayForm? _overlay;
    private long? _overlayClosedAtMs;
    private IReadOnlyList<DeviceReading> _readings = [];
    private TrayDevices? _devices; // null until the first read
    private double? _drainPercentPerDay;
    private bool _refreshing;
    private bool _exited;

    public TrayApplicationContext(IBatteryProvider provider, DeviceHistories? histories, SettingsFile settingsFile)
    {
        _displayVersion = AppVersion.Display(_informationalVersion);
        _provider = provider;
        _histories = histories;
        _settingsFile = settingsFile;
        _settings = settingsFile.Load();
        _tracker = new DeviceTracker(_settings.DeviceNames);

        _isSimulator = provider is MockBatteryProvider;
        _updateNotifier = new UpdateNotifier(_informationalVersion, () => _settings.CheckForUpdates, _isSimulator, ShowUpdateBalloon);
        if (!_isSimulator)
            AutoStart.EnableOnFirstRun();

        _showInTrayMenu = new ShowInTrayMenu(ChooseTrayDevice);
        var menu = TrayMenu.Build(
            provider: provider,
            histories: histories,
            showInTray: _showInTrayMenu.Item,
            notifications: BuildNotificationsMenu(),
            checkForUpdates: _updateNotifier.BuildCheckItem(),
            refreshWithoutWaiting: RefreshWithoutWaiting,
            historyCleared: OnHistoryCleared,
            exit: ExitApp);
        _presenter = new TrayPresenter(menu, _displayVersion, OnTrayClick, OnBalloonTipClicked);
        _scheduler = new RefreshScheduler(provider, RefreshWithoutWaiting, RefreshAsync);

        _updateNotifier.ApplySetting();
        _scheduler.Start();
    }

    // Raised for every balloon; the notifier decides whether it was the update balloon.
    private void OnBalloonTipClicked(object? sender, EventArgs e) => _updateNotifier.OnBalloonClicked();

    private ToolStripMenuItem BuildNotificationsMenu() =>
        NotificationsMenu.Build(() => _settings.Alerts, ApplyAlertSettings, () => _settings.CheckForUpdates, ToggleCheckForUpdates);

    private void OnHistoryCleared()
    {
        _drainPercentPerDay = null;
        RefreshTooltip();
    }

    private void ApplyAlertSettings(AlertSettings updated)
    {
        SaveSettings(_settings with { Alerts = updated }, ShowWarning);
        RefreshWithoutWaiting(); // a new level is checked against the current battery at once
    }

    private void ToggleCheckForUpdates()
    {
        SaveSettings(_settings with { CheckForUpdates = !_settings.CheckForUpdates }, ShowWarning);
        _updateNotifier.ApplySetting();
    }

    private void ChooseTrayDevice(DeviceId device)
    {
        if (_isSimulator)
            _mockTrayDevice = device;
        else
            SaveSettings(_settings with { TrayDevice = device }, ShowWarning);
        ShowTrayDevice(CurrentDevices(), trayHistoryGotLine: false); // the last readings are enough, no new read
    }

    private void SaveDeviceNames(IReadOnlyDictionary<DeviceId, string> names)
    {
        SaveSettings(_settings with { DeviceNames = names }, LogSettingsSaveProblem); // automatic save: no dialog
    }

    // The simulator keeps its tray choice in _mockTrayDevice and never writes settings.ini.
    private void SaveFirstTrayDevice()
    {
        if (_isSimulator)
            return;
        if (TrayDeviceChoice.FirstPickToSave(_readings, _settings.TrayDevice) is not { } device)
            return;

        SaveSettings(_settings with { TrayDevice = device }, LogSettingsSaveProblem); // automatic save: no dialog
    }

    private static void LogSettingsSaveProblem(string problem) => AppLog.Warn("Settings save failed", ("problem", problem));

    private void SaveSettings(AppSettings updated, Action<string> reportProblem)
    {
        _settings = updated; // applies for this session even when the save fails
        if (_settingsFile.Save(_settings) is { } problem)
            reportProblem(problem);
    }

    internal static void ShowWarning(string message) =>
        MessageBox.Show(message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);

    private void RefreshWithoutWaiting() =>
        BackgroundWork.RunWithoutWaiting(RefreshAsync, "Battery refresh failed");

    /// <summary>Ran is false when skipped; Status is null when skipped or the read failed.</summary>
    private async Task<(bool Ran, BatteryStatus? Status)> RefreshAsync()
    {
        if (_refreshing || _exited)
        {
            if (_refreshing && _stuckReadWatch.ShouldReport(Environment.TickCount64))
                AppLog.Warn("Battery read still running", ("seconds", (int)StuckReadAfter.TotalSeconds));
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
        var devices = CurrentDevices();
        var trayHistoryGotLine = RecordReadings(devices.Tray.Device);
        ShowTrayDevice(devices, trayHistoryGotLine);
        NotifyAlerts();
        SaveFirstTrayDevice();
        return (true, _devices?.Tray.Status);
    }

    // The simulator ignores the saved tray device and names: they belong to the real devices, which it does not show.
    private TrayDevices CurrentDevices() => _isSimulator
        ? TrayDevices.From(_readings, _mockTrayDevice, new Dictionary<DeviceId, string>(), DateTimeOffset.UtcNow)
        : TrayDevices.From(_readings, _settings.TrayDevice, _settings.DeviceNames, DateTimeOffset.UtcNow);

    // Returns true when the tray device's history got a new line.
    private bool RecordReadings(DeviceInfo trayDevice)
    {
        if (_histories is null)
            return false;

        var now = DateTimeOffset.UtcNow;
        var trayHistoryGotLine = false;
        foreach (var (device, status) in _readings)
        {
            if (status is null || device.IsPlaceholder)
                continue;
            var wrote = _histories.For(device, now).Record(status);
            var isTrayDevice = device.Id == trayDevice.Id;
            if (wrote && isTrayDevice)
                trayHistoryGotLine = true;
        }
        return trayHistoryGotLine;
    }

    // The tray device's history is null in the simulator and while no device is known.
    private BatteryHistory? HistoryOf(DeviceInfo trayDevice)
    {
        if (_histories is null || trayDevice.IsPlaceholder)
            return null;
        return _histories.For(trayDevice, DateTimeOffset.UtcNow);
    }

    // Returns true when the history in use is another one than before.
    private bool SwitchHistoryTo(DeviceInfo trayDevice)
    {
        var newHistory = HistoryOf(trayDevice);
        var isAnotherHistory = newHistory != _history;
        _history = newHistory;
        return isAnotherHistory;
    }

    // Everything that follows the tray device: history, drain rate, overlay, tooltip, icon and menu.
    private void ShowTrayDevice(TrayDevices devices, bool trayHistoryGotLine)
    {
        try
        {
            _devices = devices;
            var historyShown = SwitchHistoryTo(devices.Tray.Device) || trayHistoryGotLine;
            if (historyShown)
                RecomputeDrainRate();
            _showInTrayMenu.Update(devices.Items);
            ShowInOverlay(devices, historyShown);
            RefreshTooltip();
            _presenter.ShowIcon(devices.Tray.Status);
        }
        catch (Exception ex)
        {
            AppLog.Error("Tray update failed", AppLog.FailureWithStack(ex)); // keep the previous icon
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
            AppLog.Error("Notifications failed", AppLog.FailureWithStack(ex));
        }
    }

    private void RefreshTooltip()
    {
        if (_devices is not { } devices) // no device is known before the first refresh
            return;

        _presenter.ShowTooltip(devices, _drainPercentPerDay);
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
        overlay.FormClosed += OnOverlayClosed;
        overlay.Show();
        _overlay = overlay; // after Show, so a failing Show leaves no half-open panel behind
    }

    private void OnOverlayClosed(object? sender, FormClosedEventArgs e)
    {
        _overlay = null;
        _overlayClosedAtMs = Environment.TickCount64;
    }

    private void ShowUpdateBalloon(string title, string text) =>
        _presenter.ShowBalloon(title, text, ToolTipIcon.Info);

    private void ShowAlert(DeviceAlert deviceAlert)
    {
        var (title, text) = BatteryAlertText.For(deviceAlert.Alert, deviceAlert.Percent, deviceAlert.DeviceName);
        var icon = deviceAlert.Alert == BatteryAlert.Full ? ToolTipIcon.Info : ToolTipIcon.Warning;
        AppLog.Info("Notification", ("title", title), ("percent", deviceAlert.Percent));
        _updateNotifier.OnOtherBalloonShown();
        _presenter.ShowBalloon(title, text, icon);
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
        _overlay?.Dispose();
        _overlay = null;
        _scheduler.Stop();
        _updateNotifier.Dispose();
        _presenter.Dispose();
        ExitThread();
    }
}
