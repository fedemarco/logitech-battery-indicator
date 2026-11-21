using LogitechBattery.Alerts;
using LogitechBattery.Battery;
using LogitechBattery.History;

namespace LogitechBattery;

/// <summary>"Notifications" submenu: the low battery level, the full-charge switch and the new-version switch. Texts and check marks are set each time it opens.</summary>
internal static class NotificationsMenu
{
    public static ToolStripMenuItem Build(Func<AlertSettings> current, Action<AlertSettings> apply, Func<bool> checkForUpdates, Action toggleCheckForUpdates)
    {
        var lowItem = new ToolStripMenuItem();
        var fullItem = new ToggleMenuItem("Fully charged");
        var updatesItem = new ToggleMenuItem("New app versions");
        var lowChoices = new List<(ToggleMenuItem Item, int Percent)>();

        void RefreshMarks() => ShowCurrent(current(), lowItem, lowChoices, fullItem, checkForUpdates(), updatesItem);
        void ApplyAndRefresh(AlertSettings updated)
        {
            apply(updated);
            RefreshMarks();
        }

        void ToggleFullChargeAlert(object? sender, EventArgs e) =>
            ApplyAndRefresh(current() with { FullChargeAlert = !current().FullChargeAlert });

        void ToggleUpdateCheck(object? sender, EventArgs e)
        {
            toggleCheckForUpdates();
            RefreshMarks();
        }

        // A quick click right after passing over "Low battery" was lost while its submenu was still open (seen on Windows),
        // so the submenu closes as soon as the pointer reaches one of the switches.
        void HideLowBatterySubmenu(object? sender, EventArgs e) => lowItem.HideDropDown();

        void RefreshNotificationMarks(object? sender, EventArgs e) => RefreshMarks();

        foreach (var percent in AlertSettings.LowChoices)
        {
            void ChooseLowPercent(object? sender, EventArgs e) => ApplyAndRefresh(current() with { LowPercent = percent });

            var choice = new ToggleMenuItem($"{percent}%");
            choice.Click += ChooseLowPercent;
            lowChoices.Add((choice, percent));
            lowItem.DropDownItems.Add(choice);
        }
        fullItem.Click += ToggleFullChargeAlert;
        updatesItem.Click += ToggleUpdateCheck;
        fullItem.MouseEnter += HideLowBatterySubmenu;
        updatesItem.MouseEnter += HideLowBatterySubmenu;

        var menu = new ToolStripMenuItem("Notifications");
        menu.DropDownItems.Add(lowItem);
        menu.DropDownItems.Add(fullItem);
        menu.DropDownItems.Add(updatesItem);
        menu.DropDownOpening += RefreshNotificationMarks;
        return menu;
    }

    private static void ShowCurrent(
        AlertSettings settings,
        ToolStripMenuItem lowItem,
        IReadOnlyList<(ToggleMenuItem Item, int Percent)> lowChoices,
        ToolStripMenuItem fullItem,
        bool checkForUpdates,
        ToolStripMenuItem updatesItem)
    {
        lowItem.Text = $"Low battery: {settings.LowPercent}%"; // a screen reader reads the value before the list opens
        foreach (var choice in lowChoices)
            choice.Item.Checked = choice.Percent == settings.LowPercent;
        fullItem.Checked = settings.FullChargeAlert;
        updatesItem.Checked = checkForUpdates;
    }
}
