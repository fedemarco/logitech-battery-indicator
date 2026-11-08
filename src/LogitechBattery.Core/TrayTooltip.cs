namespace LogitechBattery;

/// <summary>The text of the tray icon's tooltip: status, estimate and app name with version, one per line.</summary>
public static class TrayTooltip
{
    /// <summary>NotifyIcon.MaxTextSize in dotnet/winforms (szTip holds 128 with the terminator); a longer text makes NotifyIcon.Text throw.</summary>
    public const int MaxLength = 127;

    public static string Build(string? statusLine, string? remainingText, string displayVersion)
    {
        var lines = new[] { statusLine, remainingText, $"Logitech Battery {displayVersion}" }.OfType<string>();
        var text = string.Join('\n', lines);
        return text.Length > MaxLength ? text[..MaxLength] : text;
    }
}
