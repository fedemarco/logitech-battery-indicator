using System.Reflection;

namespace LogitechBattery;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _notifyIcon;

    public TrayApplicationContext()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Exit", null, (_, _) => ExitApp());

        _notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = BuildTooltip(),
            ContextMenuStrip = menu,
            Visible = true,
        };
    }

    private static string BuildTooltip()
    {
        var version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";

        // Shorten "+<sha>" build metadata to 7 characters.
        var plus = version.IndexOf('+');
        if (plus >= 0 && version.Length - plus - 1 > 7)
            version = version[..(plus + 1 + 7)];

        var text = $"Logitech Battery {version}";
        return text.Length > 63 ? text[..63] : text;
    }

    private void ExitApp()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        ExitThread();
    }
}
