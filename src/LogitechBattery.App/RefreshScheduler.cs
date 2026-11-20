using Microsoft.Win32;
using LogitechBattery.Battery;
using Timer = System.Windows.Forms.Timer;

namespace LogitechBattery;

/// <summary>Decides when the battery is read: a poll timer, follow-up reads after a change, the first idle and a PC resume.</summary>
internal sealed class RefreshScheduler
{
    private const int MockPollIntervalMs = 1000;
    private const int PollIntervalMs = 60_000;

    private readonly IBatteryProvider _provider;
    // The poll tick starts a read and does not wait; the follow-up needs the read's result to plan the next read.
    private readonly Action _refreshWithoutWaiting;
    private readonly Func<Task<(bool Ran, BatteryStatus? Status)>> _refreshAsync;
    private readonly FollowUpSchedule _followUpSchedule = new();
    private readonly Timer _pollTimer = new();
    private readonly Timer _followUpTimer = new();
    private volatile SynchronizationContext? _uiContext;
    private bool _stopped;

    public RefreshScheduler(
        IBatteryProvider provider,
        Action refreshWithoutWaiting,
        Func<Task<(bool Ran, BatteryStatus? Status)>> refreshAsync)
    {
        _provider = provider;
        _refreshWithoutWaiting = refreshWithoutWaiting;
        _refreshAsync = refreshAsync;
        if (SynchronizationContext.Current is WindowsFormsSynchronizationContext windowsFormsContext)
            _uiContext = windowsFormsContext; // otherwise captured on the first idle
    }

    public void Start()
    {
        _pollTimer.Interval = _provider is MockBatteryProvider ? MockPollIntervalMs : PollIntervalMs;
        _pollTimer.Tick += OnPollTimerTick;
        _pollTimer.Start();
        _followUpTimer.Tick += OnFollowUpTick;
        if (_provider is HidPpBatteryProvider hidProvider)
        {
            hidProvider.ChangeDetected += OnChangeDetected;
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
        }

        Application.Idle += OnFirstIdle;
    }

    public void Stop()
    {
        _stopped = true;
        if (_provider is HidPpBatteryProvider hidProvider)
        {
            hidProvider.ChangeDetected -= OnChangeDetected;
            SystemEvents.PowerModeChanged -= OnPowerModeChanged; // static event: it would keep this object alive and run after exit
        }
        _pollTimer.Stop();
        _pollTimer.Dispose();
        _followUpTimer.Stop();
        _followUpTimer.Dispose();
    }

    private void OnPollTimerTick(object? sender, EventArgs e) => _refreshWithoutWaiting();

    // First refresh once the message loop runs, so awaited reads resume on the UI thread.
    private void OnFirstIdle(object? sender, EventArgs e)
    {
        Application.Idle -= OnFirstIdle;
        _uiContext ??= SynchronizationContext.Current;
        _refreshWithoutWaiting();
    }

    // Raised on a background thread. Before the first idle there is no UI context yet; dropping the change is safe then,
    // because the first idle starts a refresh anyway.
    private void OnChangeDetected() => _uiContext?.Post(RestartFollowUpOnUiThread, null);

    private void RestartFollowUpOnUiThread(object? state)
    {
        if (_stopped)
            return;
        StartFollowUpTimer(_followUpSchedule.Restart());
    }

    // Raised on a system thread: it writes the log, reopens the devices there, and posts to the UI thread through OnChangeDetected.
    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        switch (e.Mode)
        {
            case PowerModes.Suspend:
                AppLog.Info("PC suspending");
                break;
            case PowerModes.Resume:
                AppLog.Info("PC resumed");
                if (_provider is HidPpBatteryProvider hidProvider) // the simulator has no devices to reopen
                    hidProvider.ReopenDevices();
                OnChangeDetected(); // reads again after 1 s, then follows up like after any change
                break;
        }
    }

    private void OnFollowUpTick(object? sender, EventArgs e) =>
        BackgroundWork.RunWithoutWaiting(FollowUpReadAsync, "Follow-up read failed");

    private async Task FollowUpReadAsync()
    {
        _followUpTimer.Stop();
        var generation = _followUpSchedule.Generation;
        var (ran, status) = await _refreshAsync();
        if (_stopped)
            return;

        if (_followUpSchedule.Finish(generation, ran, status) is { } delayMs)
            StartFollowUpTimer(delayMs);
    }

    private void StartFollowUpTimer(int delayMs)
    {
        _followUpTimer.Stop();
        _followUpTimer.Interval = delayMs;
        _followUpTimer.Start();
    }
}
