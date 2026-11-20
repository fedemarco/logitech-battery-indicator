namespace LogitechBattery;

/// <summary>Windows tray menus close after any click. Choices stay open so that several can be changed and the check mark is seen; actions close the menu.</summary>
internal static class MenuStaysOpenForToggles
{
    public static void Attach(ContextMenuStrip menu)
    {
        // One flag for the menu and all its submenus: a click in a submenu also closes the parent dropdowns.
        var lastClickWasToggle = false;
        AttachToDropDown(menu, menu.Items);

        void AttachToDropDown(ToolStripDropDown dropDown, ToolStripItemCollection items)
        {
            dropDown.ItemClicked += (_, e) =>
            {
                lastClickWasToggle = e.ClickedItem is ToggleMenuItem;
                AppLog.Info("Menu trace", ("event", "itemClicked"), ("item", e.ClickedItem?.Text), ("toggle", lastClickWasToggle));
            };
            dropDown.Closing += (_, e) =>
            {
                if (e.CloseReason == ToolStripDropDownCloseReason.ItemClicked && lastClickWasToggle)
                    e.Cancel = true;
                AppLog.Info("Menu trace", ("event", "closing"), ("reason", e.CloseReason), ("cancel", e.Cancel));
            };
            dropDown.Closed += (_, e) => AppLog.Info("Menu trace", ("event", "closed"), ("reason", e.CloseReason));
            // Only items that own a submenu: reading DropDown on a leaf item would create an empty one for nothing.
            foreach (var submenu in items.OfType<ToolStripMenuItem>().Where(item => item.HasDropDown))
                AttachToDropDown(submenu.DropDown, submenu.DropDownItems);
        }
    }
}
