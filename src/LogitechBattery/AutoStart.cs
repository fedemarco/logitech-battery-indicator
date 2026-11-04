using Microsoft.Win32;

namespace LogitechBattery;

/// <summary>"Start with Windows" via the per-user Run key (no admin). Never throws.</summary>
internal static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "LogitechBattery";
    private const string AppKey = @"Software\LogitechBattery";
    private const string InitializedValue = "AutoStartInitialized";

    private static string? Command => Environment.ProcessPath is { } path ? $"\"{path}\"" : null;

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string value
                && Command is { } command
                && string.Equals(value, command, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            AppLog.Write($"Reading start with Windows failed: {ex.Message}");
            return false;
        }
    }

    public static void Enable()
    {
        try
        {
            if (Command is not { } command)
                return;
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            key.SetValue(ValueName, command);
        }
        catch (Exception ex)
        {
            AppLog.Write($"Enabling start with Windows failed: {ex.Message}");
        }
    }

    public static void Disable()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch (Exception ex)
        {
            AppLog.Write($"Disabling start with Windows failed: {ex.Message}");
        }
    }

    /// <summary>Turns it on the first time the app runs; afterwards the user's choice is respected.</summary>
    public static void EnableOnFirstRun()
    {
        try
        {
            using var app = Registry.CurrentUser.CreateSubKey(AppKey);
            if (app.GetValue(InitializedValue) is not null)
                return;
            Enable();
            app.SetValue(InitializedValue, 1, RegistryValueKind.DWord);
        }
        catch (Exception ex)
        {
            AppLog.Write($"First-run start with Windows failed: {ex.Message}");
        }
    }
}
