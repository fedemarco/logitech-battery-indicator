using LogitechBattery.Battery;

namespace LogitechBattery;

/// <summary>"Show in tray" submenu: one entry per device. It is visible only with several devices; the entries are filled each time it opens.</summary>
internal sealed class ShowInTrayMenu
{
    private const int MinDevicesToShow = 2;

    private readonly Action<DeviceId> _choose;
    private IReadOnlyList<TrayDeviceItem> _items = [];

    public ShowInTrayMenu(Action<DeviceId> choose)
    {
        _choose = choose;
        Item = new ToolStripMenuItem("Show in tray") { Visible = false };
        Item.DropDownOpening += ShowDeviceChoices;
        _ = Item.DropDown; // its entries come only when it opens, so the submenu is created now for MenuStaysOpenForToggles to find
    }

    public ToolStripMenuItem Item { get; }

    public void Update(IReadOnlyList<TrayDeviceItem> items)
    {
        _items = items;
        Item.Visible = items.Count >= MinDevicesToShow;
    }

    private void ShowDeviceChoices(object? sender, EventArgs e)
    {
        Item.DropDownItems.Clear();
        foreach (var device in _items)
        {
            var choice = new ToggleMenuItem(device.Text) { Checked = device.IsTray };

            void OnDeviceChoiceClicked(object? sender, EventArgs e)
            {
                _choose(device.Id);
                ShowOnlyChecked(choice);
            }

            choice.Click += OnDeviceChoiceClicked;
            Item.DropDownItems.Add(choice);
        }
    }

    private void ShowOnlyChecked(ToolStripMenuItem chosen)
    {
        foreach (ToolStripMenuItem choice in Item.DropDownItems)
            choice.Checked = choice == chosen;
    }
}
