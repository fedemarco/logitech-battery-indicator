using System.Diagnostics;
using LogitechBattery.Logging;
using LogitechBattery.Updates;
using Timer = System.Windows.Forms.Timer;

namespace LogitechBattery;

/// <summary>
/// Everything about new app versions: the daily automatic check (only while the setting is on), the "Check for updates…" item,
/// the balloon and the result boxes. The app never downloads or installs anything; it only opens the release page.
/// </summary>
internal sealed class UpdateNotifier : IDisposable
{
    private const int FirstCheckDelayMs = 2 * 60 * 1000;
    private const int CheckIntervalMs = 24 * 60 * 60 * 1000;
    private const string AutomaticTrigger = "automatic";
    private const string ManualTrigger = "manual";
    private const string UpdateCheckFailedMessage = "Update check failed";

    private readonly string? _informationalVersion;
    private readonly Func<bool> _isEnabled;
    private readonly bool _isSimulator;
    private readonly Action<string, string> _showBalloon;
    private readonly UpdateCheck _updateCheck = new();
    private Timer? _timer; // exists only while the setting is on
    private Version? _announcedVersion; // one balloon per version per session
    private string? _pageOfBalloon; // set while the balloon on screen is the update balloon
    private bool _disposed;

    /// <param name="isEnabled">Whether the user turned on "New app versions".</param>
    /// <param name="showBalloon">Shows an info balloon with a title and a text.</param>
    public UpdateNotifier(string? informationalVersion, Func<bool> isEnabled, bool isSimulator, Action<string, string> showBalloon)
    {
        _informationalVersion = informationalVersion;
        _isEnabled = isEnabled;
        _isSimulator = isSimulator;
        _showBalloon = showBalloon;
    }

    /// <summary>Starts or stops the automatic check to match the setting. The simulator makes no automatic request: the CI smoke test runs it.</summary>
    public void ApplySetting()
    {
        StopTimer();
        if (!_isEnabled() || _isSimulator)
            return;

        _timer = new Timer { Interval = FirstCheckDelayMs };
        _timer.Tick += OnTimerTick;
        _timer.Start();
    }

    public ToolStripMenuItem BuildCheckItem()
    {
        var item = new ToolStripMenuItem("Check for updates…");

        async Task CheckManuallyThenEnableItemAsync()
        {
            try
            {
                await CheckManuallyAsync();
            }
            finally
            {
                item.Enabled = true;
            }
        }

        void CheckForUpdatesFromMenu(object? sender, EventArgs e)
        {
            item.Enabled = false;
            BackgroundWork.RunWithoutWaiting(CheckManuallyThenEnableItemAsync, UpdateCheckFailedMessage);
        }

        item.Click += CheckForUpdatesFromMenu;
        return item;
    }

    /// <summary>Balloon clicks arrive for every balloon, so this opens the page only when the last balloon was the update balloon.</summary>
    public void OnBalloonClicked()
    {
        if (_pageOfBalloon is { } pageUrl)
            OpenReleasePage(pageUrl);
    }

    /// <summary>Another balloon replaced the update balloon.</summary>
    public void OnOtherBalloonShown() => _pageOfBalloon = null;

    public void Dispose()
    {
        _disposed = true;
        StopTimer();
    }

    private void StopTimer()
    {
        _timer?.Stop();
        _timer?.Dispose();
        _timer = null;
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        _timer!.Interval = CheckIntervalMs; // the first check came 2 minutes after the start, the next ones follow daily
        BackgroundWork.RunWithoutWaiting(CheckAutomaticallyAsync, UpdateCheckFailedMessage);
    }

    private async Task CheckAutomaticallyAsync()
    {
        var result = await _updateCheck.CheckAsync(_informationalVersion, CancellationToken.None);
        if (_disposed || !_isEnabled()) // switched off while the request ran
            return;

        LogResult(result, AutomaticTrigger);
        if (result is UpdateCheckResult.Available available)
            Announce(available);
    }

    private async Task CheckManuallyAsync()
    {
        var result = await _updateCheck.CheckAsync(_informationalVersion, CancellationToken.None);
        if (_disposed)
            return;

        LogResult(result, ManualTrigger);
        ShowResult(result);
    }

    private static void LogResult(UpdateCheckResult result, string trigger)
    {
        switch (result)
        {
            case UpdateCheckResult.UpToDate upToDate:
                AppLog.Info("Update check", ("result", "upToDate"), ("version", upToDate.Current), ("trigger", trigger));
                break;
            case UpdateCheckResult.Available available:
                AppLog.Info("Update check", ("result", "available"), ("version", available.Version), ("trigger", trigger));
                break;
            case UpdateCheckResult.Failed failed:
                AppLog.Warn("Update check failed", ("trigger", trigger), ("reason", failed.Reason));
                break;
        }
    }

    private void Announce(UpdateCheckResult.Available available)
    {
        if (available.Version == _announcedVersion)
            return;

        _announcedVersion = available.Version;
        _pageOfBalloon = available.PageUrl;
        _showBalloon($"{TrayApplicationContext.AppName} {available.Version} is available", "Click to open the download page.");
    }

    private static void ShowResult(UpdateCheckResult result)
    {
        switch (result)
        {
            case UpdateCheckResult.Available available:
                var answer = MessageBox.Show(
                    $"Version {available.Version} is available. Open the download page?",
                    TrayApplicationContext.AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                if (answer == DialogResult.Yes)
                    OpenReleasePage(available.PageUrl);
                break;
            case UpdateCheckResult.UpToDate upToDate:
                MessageBox.Show(
                    $"You have the latest version ({upToDate.Current}).",
                    TrayApplicationContext.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                break;
            case UpdateCheckResult.Failed failed:
                TrayApplicationContext.ShowWarning($"Couldn't check for updates: {failed.Reason.TrimEnd('.')}."); // a network error message may end with its own period
                break;
        }
    }

    private static void OpenReleasePage(string pageUrl)
    {
        try
        {
            // Start returns null when the shell reused an already running process; there is nothing to dispose then.
            using var started = Process.Start(new ProcessStartInfo(pageUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLog.Warn("Opening the download page failed", AppLog.Failure(ex)); // no default browser, for example
            TrayApplicationContext.ShowWarning($"Couldn't open the download page. Its address is:\n{pageUrl}"); // only after a click, so the user is waiting for it
        }
    }
}
