namespace LogitechBattery;

/// <summary>A check-mark item (a switch or one choice of a list). <see cref="MenuStaysOpenForToggles"/> keeps the menu open after a click on it.</summary>
internal sealed class ToggleMenuItem : ToolStripMenuItem
{
    public ToggleMenuItem(string text) : base(text)
    {
    }
}
