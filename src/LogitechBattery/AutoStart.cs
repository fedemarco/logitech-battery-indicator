using Microsoft.Win32;

namespace LogitechBattery;

/// <summary>"Start with Windows" via the per-user Run key (no admin). Never throws.</summary>
internal static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    // Task Manager's "Startup apps" toggle: a binary value whose first byte is odd when disabled.
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ExeName = "LogitechBattery.exe";
    private const string ValueName = "LogitechBattery";
    private const string AppKey = @"Software\LogitechBattery";
    private const string InitializedValue = "AutoStartInitialized";

    private static string? Command => Environment.ProcessPath is { } path ? $"\"{path}\"" : null;

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            if (key?.GetValue(ValueName) is not string value
                || Command is not { } command
                || !string.Equals(value, command, StringComparison.OrdinalIgnoreCase))
                return false;
            using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey);
            return approved?.GetValue(ValueName) is not byte[] { Length: > 0 } flags || (flags[0] & 1) == 0;
        }
        catch (Exception ex)
        {
            AppLog.Write($"Reading start with Windows failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>Writes the Run entry and clears a Task Manager "disabled" flag. Returns false on failure.</summary>
    public static bool Enable()
    {
        try
        {
            if (Command is not { } command)
                return false;
            using (var key = Registry.CurrentUser.CreateSubKey(RunKey))
                key.SetValue(ValueName, command);
            using (var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey, writable: true))
                approved?.DeleteValue(ValueName, throwOnMissingValue: false);
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Write($"Enabling start with Windows failed: {ex.Message}");
            return false;
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
            // Only the real app registers itself (not e.g. dotnet.exe during development).
            if (!string.Equals(Path.GetFileName(Environment.ProcessPath), ExeName, StringComparison.OrdinalIgnoreCase))
                return;
            if (Enable())
                app.SetValue(InitializedValue, 1, RegistryValueKind.DWord); // retried next start if writing failed
        }
        catch (Exception ex)
        {
            AppLog.Write($"First-run start with Windows failed: {ex.Message}");
        }
    }
}
