using LogitechBattery.Battery;
using LogitechBattery.History;
using LogitechBattery.Tray;

namespace LogitechBattery;

/// <summary>Owns the tray icon and what it shows: the battery icon, the tooltip and the balloons.</summary>
internal sealed class TrayPresenter : IDisposable
{
    private const int BalloonTimeoutMs = 10_000;

    private readonly NotifyIcon _notifyIcon;
    private readonly string _displayVersion;
    private Icon? _icon;
    private int _iconSize;
    private TrayIndicator? _indicator;

    public TrayPresenter(ContextMenuStrip menu, string displayVersion, MouseEventHandler onClick, EventHandler onBalloonClicked)
    {
        _displayVersion = displayVersion;
        _notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application, // replaced by the first refresh
            Text = TrayTooltip.Build(
                DeviceInfo.NoDeviceYet.Name, statusLine: null, remainingText: null, otherDevices: [], displayVersion),
            ContextMenuStrip = menu,
            Visible = true,
        };
        _notifyIcon.MouseClick += onClick;
        _notifyIcon.BalloonTipClicked += onBalloonClicked;
    }

    public void ShowIcon(BatteryStatus? status)
    {
        var indicator = TrayIndicator.From(status);
        var iconSize = TrayIconSize.Current(); // checked each time: the scaling can change while the app runs
        if (indicator != _indicator || iconSize != _iconSize)
            ReplaceIcon(indicator, iconSize);
    }

    public void ShowTooltip(TrayDevices devices, double? drainPercentPerDay)
    {
        var remainingText = devices.Tray.Status is null ? null : RemainingTime.RemainingText(devices.Tray.Status, drainPercentPerDay);
        _notifyIcon.Text = TrayTooltip.Build(devices.TrayName, devices.StatusLine, remainingText, devices.Others, _displayVersion);
    }

    public void ShowBalloon(string title, string text, ToolTipIcon icon) =>
        _notifyIcon.ShowBalloonTip(BalloonTimeoutMs, title, text, icon);

    public void Dispose()
    {
        _notifyIcon.Visible = false; // hide first to avoid a ghost icon
        _notifyIcon.Dispose();
        _icon?.Dispose();
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
}
