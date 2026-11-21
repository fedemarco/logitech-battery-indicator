namespace LogitechBattery.App.Tests;

internal static class MenuLookup
{
    private const string SeparatorLabel = "-";

    public static readonly Point ShownMenuLocation = new(100, 100);

    public static ToolStripMenuItem ItemNamed(this ToolStripItemCollection items, string text) =>
        items.OfType<ToolStripMenuItem>().Single(item => item.Text == text);

    public static List<string> Labels(this ToolStripItemCollection items) =>
        items.Cast<ToolStripItem>().Select(LabelOf).ToList();

    private static string LabelOf(ToolStripItem item) => item is ToolStripSeparator ? SeparatorLabel : item.Text ?? string.Empty;
}
