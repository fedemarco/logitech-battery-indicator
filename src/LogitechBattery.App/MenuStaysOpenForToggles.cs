namespace LogitechBattery;

/// <summary>Windows tray menus close after any click. Choices stay open so that several can be changed and the check mark is seen; actions close the menu.</summary>
internal static class MenuStaysOpenForToggles
{
    public static void Attach(ContextMenuStrip menu)
    {
        // One flag for the menu and all its submenus: a click in a submenu also closes the parent dropdowns.
        var lastClickWasToggle = false;
        AttachToDropDown(menu, menu.Items);

        void OnItemClicked(object? sender, ToolStripItemClickedEventArgs e) =>
            lastClickWasToggle = e.ClickedItem is ToggleMenuItem;

        void OnClosing(object? sender, ToolStripDropDownClosingEventArgs e)
        {
            if (e.CloseReason == ToolStripDropDownCloseReason.ItemClicked && lastClickWasToggle)
                e.Cancel = true;
        }

        void AttachToDropDown(ToolStripDropDown dropDown, ToolStripItemCollection items)
        {
            dropDown.ItemClicked += OnItemClicked;
            dropDown.Closing += OnClosing;
            // Only items that own a submenu: reading DropDown on a leaf item would create an empty one for nothing.
            foreach (var submenu in items.OfType<ToolStripMenuItem>().Where(item => item.HasDropDown))
                AttachToDropDown(submenu.DropDown, submenu.DropDownItems);
        }
    }
}
