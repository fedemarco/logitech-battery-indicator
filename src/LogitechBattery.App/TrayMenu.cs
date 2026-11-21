using System.ComponentModel;
using LogitechBattery.Battery;

namespace LogitechBattery;

/// <summary>The tray icon's context menu. The simulator gets its own entries; a real provider gets the start-with-Windows and clear-history entries.</summary>
internal static class TrayMenu
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

    public static ContextMenuStrip Build(
        IBatteryProvider provider,
        DeviceHistories? histories,
        ToolStripItem showInTray,
        ToolStripItem notifications,
        ToolStripItem checkForUpdates,
        Action refreshWithoutWaiting,
        Action historyCleared,
        Action exit)
    {
        void OnExitClicked(object? sender, EventArgs e) => exit();

        var menu = new ContextMenuStrip();
        if (provider is MockBatteryProvider mock)
            menu.Items.Add(BuildSimulatorMenu(mock, refreshWithoutWaiting));
        menu.Items.Add(showInTray);
        menu.Items.Add(notifications);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(checkForUpdates);
        if (provider is not MockBatteryProvider) // the simulator has no real start-with-Windows or history
        {
            var startItem = BuildStartupItem();

            void RefreshStartWithWindowsMark(object? sender, CancelEventArgs e) => startItem.Checked = AutoStart.IsEnabled();

            menu.Opening += RefreshStartWithWindowsMark;
            menu.Items.Add(startItem);
            if (histories is not null)
                menu.Items.Add(BuildClearHistoryItem(histories, historyCleared));
        }

        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += OnExitClicked;
        menu.Items.Add(exitItem);
        MenuStaysOpenForToggles.Attach(menu);
        MenuKeepsScreenEdge.Attach(menu);
        return menu;
    }

    private static ToolStripMenuItem BuildSimulatorMenu(MockBatteryProvider mock, Action refreshWithoutWaiting)
    {
        var simulator = new ToolStripMenuItem("Simulator");
        var modeItems = new List<(ToolStripMenuItem Item, MockMode Mode)>();
        foreach (var (label, mode) in SimulatorItems)
        {
            var item = new ToggleMenuItem(label) { Checked = mock.Mode == mode };

            void OnSimulatorModeClicked(object? sender, EventArgs e)
            {
                mock.Mode = mode;
                foreach (var other in modeItems)
                    other.Item.Checked = other.Mode == mode;
                refreshWithoutWaiting();
            }

            item.Click += OnSimulatorModeClicked;
            modeItems.Add((item, mode));
            simulator.DropDownItems.Add(item);
        }
        simulator.DropDownItems.Add(new ToolStripSeparator());
        simulator.DropDownItems.Add(BuildSeveralDevicesItem(mock, refreshWithoutWaiting));
        return simulator;
    }

    private static ToolStripMenuItem BuildSeveralDevicesItem(MockBatteryProvider mock, Action refreshWithoutWaiting)
    {
        var item = new ToggleMenuItem("Several devices") { Checked = mock.SeveralDevices };

        void OnSeveralDevicesClicked(object? sender, EventArgs e)
        {
            mock.SeveralDevices = !mock.SeveralDevices;
            item.Checked = mock.SeveralDevices;
            refreshWithoutWaiting();
        }

        item.Click += OnSeveralDevicesClicked;
        return item;
    }

    private static ToolStripMenuItem BuildStartupItem()
    {
        var startItem = new ToggleMenuItem("Start with Windows");

        void OnStartWithWindowsClicked(object? sender, EventArgs e)
        {
            ToggleStartWithWindows();
            startItem.Checked = AutoStart.IsEnabled();
        }

        startItem.Click += OnStartWithWindowsClicked;
        return startItem;
    }

    private static void ToggleStartWithWindows()
    {
        if (AutoStart.IsEnabled())
        {
            if (!AutoStart.Disable())
                TrayApplicationContext.ShowWarning("Couldn't turn off Start with Windows. The reason is in log.txt.");
            return;
        }

        if (!AutoStart.Enable()) // also rewrites a stale path and clears a Task Manager "disabled"
            TrayApplicationContext.ShowWarning("Couldn't turn on Start with Windows. The reason is in log.txt.");
    }

    private static ToolStripMenuItem BuildClearHistoryItem(DeviceHistories histories, Action historyCleared)
    {
        var clearItem = new ToolStripMenuItem("Clear history…");

        void OnClearHistoryClicked(object? sender, EventArgs e) => ConfirmAndClearHistory(histories, historyCleared);

        clearItem.Click += OnClearHistoryClicked;
        return clearItem;
    }

    private static void ConfirmAndClearHistory(DeviceHistories histories, Action historyCleared)
    {
        var answer = MessageBox.Show(
            "Delete all battery history? This cannot be undone.",
            TrayApplicationContext.AppName,
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (answer != DialogResult.Yes)
            return;

        var cleared = histories.ClearAll();
        if (cleared)
            historyCleared();
        else
            TrayApplicationContext.ShowWarning("Couldn't delete the battery history. Close any program that has a history file open and try again.");
    }
}
