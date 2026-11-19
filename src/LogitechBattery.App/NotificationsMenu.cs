using LogitechBattery.Battery;

namespace LogitechBattery;

/// <summary>"Notifications" submenu: the low battery level, the full-charge switch and the new-version switch. Texts and check marks are set each time it opens.</summary>
internal static class NotificationsMenu
{
    public static ToolStripMenuItem Build(Func<AlertSettings> current, Action<AlertSettings> apply, Func<bool> checkForUpdates, Action toggleCheckForUpdates)
    {
        var lowItem = new ToolStripMenuItem();
        foreach (var percent in AlertSettings.LowChoices)
        {
            var choice = new ToolStripMenuItem($"{percent}%") { Tag = percent };
            choice.Click += (_, _) => apply(current() with { LowPercent = percent });
            lowItem.DropDownItems.Add(choice);
        }

        var fullItem = new ToolStripMenuItem("Fully charged");
        fullItem.Click += (_, _) => apply(current() with { FullChargeAlert = !current().FullChargeAlert });

        var updatesItem = new ToolStripMenuItem("New app versions");
        updatesItem.Click += (_, _) => toggleCheckForUpdates();

        var menu = new ToolStripMenuItem("Notifications");
        menu.DropDownItems.Add(lowItem);
        menu.DropDownItems.Add(fullItem);
        menu.DropDownItems.Add(updatesItem);
        menu.DropDownOpening += (_, _) => ShowCurrent(current(), lowItem, fullItem, checkForUpdates(), updatesItem);
        return menu;
    }

    private static void ShowCurrent(
        AlertSettings settings, ToolStripMenuItem lowItem, ToolStripMenuItem fullItem, bool checkForUpdates, ToolStripMenuItem updatesItem)
    {
        lowItem.Text = $"Low battery: {settings.LowPercent}%"; // a screen reader reads the value before the list opens
        foreach (ToolStripMenuItem choice in lowItem.DropDownItems)
            choice.Checked = (int)choice.Tag! == settings.LowPercent;
        fullItem.Checked = settings.FullChargeAlert;
        updatesItem.Checked = checkForUpdates;
    }
}
