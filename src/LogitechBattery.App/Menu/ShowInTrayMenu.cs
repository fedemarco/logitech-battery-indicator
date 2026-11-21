using LogitechBattery.Battery;
using LogitechBattery.History;
using LogitechBattery.Tray;

namespace LogitechBattery.Menu;

/// <summary>"Show in tray" submenu: one entry per device. It is visible only with several devices; the entries are kept up to date after every refresh, so the submenu arrow shows from the start.</summary>
internal sealed class ShowInTrayMenu
{
    private const int MinDevicesToShow = 2;

    private readonly Action<DeviceId> _choose;
    private readonly List<ToggleMenuItem> _entries = [];
    private IReadOnlyList<TrayDeviceItem> _items = [];

    public ShowInTrayMenu(Action<DeviceId> choose)
    {
        _choose = choose;
        Item = new ToolStripMenuItem("Show in tray") { Visible = false };
        // MenuStaysOpenForToggles looks only at items that already own a submenu, and it runs before the first refresh.
        Item.DropDown = new ToolStripDropDownMenu();
    }

    public ToolStripMenuItem Item { get; }

    public void Update(IReadOnlyList<TrayDeviceItem> items)
    {
        var sameDevices = items.Select(device => device.Id).SequenceEqual(_items.Select(device => device.Id));
        _items = items;
        Item.Visible = items.Count >= MinDevicesToShow;

        if (sameDevices)
            UpdateEntriesInPlace();
        else
            RebuildEntries();
    }

    // No rebuild here: the menu may be open, and new entries would make it flicker.
    private void UpdateEntriesInPlace()
    {
        for (var index = 0; index < _items.Count; index++)
        {
            _entries[index].Text = _items[index].Text;
            _entries[index].Checked = _items[index].IsTray;
        }
    }

    private void RebuildEntries()
    {
        Item.DropDownItems.Clear();
        _entries.Clear();
        for (var index = 0; index < _items.Count; index++)
        {
            var entry = CreateEntry(index);
            _entries.Add(entry);
            Item.DropDownItems.Add(entry);
        }
    }

    // The click reads _items at click time: an in-place update replaces the device behind an entry, whose position stays.
    private ToggleMenuItem CreateEntry(int index)
    {
        var entry = new ToggleMenuItem(_items[index].Text) { Checked = _items[index].IsTray };

        void OnDeviceChoiceClicked(object? sender, EventArgs e)
        {
            _choose(_items[index].Id);
            ShowOnlyChecked(entry);
        }

        entry.Click += OnDeviceChoiceClicked;
        return entry;
    }

    private void ShowOnlyChecked(ToggleMenuItem chosen)
    {
        foreach (var entry in _entries)
            entry.Checked = entry == chosen;
    }
}
