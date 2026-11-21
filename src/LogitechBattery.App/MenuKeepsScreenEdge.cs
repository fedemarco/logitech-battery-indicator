using LogitechBattery.Tray;

namespace LogitechBattery;

/// <summary>The tray menu keeps the edge nearest the screen edge when its size changes while it is open, so it does not grow over the taskbar.</summary>
internal static class MenuKeepsScreenEdge
{
    public static void Attach(ContextMenuStrip menu)
    {
        var isOpen = false;
        var sizeBeforeResize = menu.Size;
        menu.Opened += OnOpened;
        menu.Closed += OnClosed;
        menu.SizeChanged += OnSizeChanged;

        void OnOpened(object? sender, EventArgs e) => isOpen = true;

        void OnClosed(object? sender, ToolStripDropDownClosedEventArgs e) => isOpen = false;

        void OnSizeChanged(object? sender, EventArgs e)
        {
            // WinForms runs pending layout during Show, before Opened fires, and positions the menu itself after that.
            // Only a resize of a menu that is already open needs moving.
            if (isOpen)
                MoveToKeepScreenEdge();
            sizeBeforeResize = menu.Size;
        }

        void MoveToKeepScreenEdge()
        {
            // WinForms keeps the top-left of an open dropdown when its items change, so Location is still the one before the resize.
            var boundsBeforeResize = new Rectangle(menu.Location, sizeBeforeResize);
            var workingArea = Screen.FromRectangle(boundsBeforeResize).WorkingArea;
            // shortcut: an open submenu keeps its screen position when the root moves, so it may not line up with its item
            // until it is reopened; upgrade if users see it.
            menu.Location = MenuPlacement.LocationAfterResize(boundsBeforeResize, menu.Size, workingArea);
        }
    }
}
