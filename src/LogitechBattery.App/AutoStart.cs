using Microsoft.Win32;

namespace LogitechBattery;

/// <summary>"Start with Windows" via the per-user Run key (no admin). Never throws.</summary>
internal static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ExeName = "LogitechBattery.exe";
    private const string ValueName = "LogitechBattery";
    private const string AppSettingsKey = @"Software\LogitechBattery";
    private const string InitializedValue = "AutoStartInitialized";

    private static string? Command => Environment.ProcessPath is { } path ? $"\"{path}\"" : null;

    public static bool IsEnabled()
    {
        try
        {
            using var runKey = Registry.CurrentUser.OpenSubKey(RunKey);
            if (runKey?.GetValue(ValueName) is not string value
                || Command is not { } command
                || !string.Equals(value, command, StringComparison.OrdinalIgnoreCase))
                return false;
            using var approvedKey = Registry.CurrentUser.OpenSubKey(StartupApprovedKey);
            return IsApprovedInStartupApps(approvedKey?.GetValue(ValueName));
        }
        catch (Exception ex)
        {
            AppLog.Write($"Reading start with Windows failed: {ex.Message}");
            return false;
        }
    }

    // Task Manager's "Startup apps" toggle stores a binary value whose first byte is odd when the entry is disabled; no value means enabled.
    private static bool IsApprovedInStartupApps(object? value) =>
        value is not byte[] { Length: > 0 } flags || (flags[0] & 1) == 0;

    /// <summary>Writes the Run entry and clears a Task Manager "disabled" flag. Returns false on failure.</summary>
    public static bool Enable()
    {
        try
        {
            if (Command is not { } command)
            {
                AppLog.Write("Enabling start with Windows failed: the path of the exe is unknown");
                return false;
            }
            using var runKey = Registry.CurrentUser.CreateSubKey(RunKey);
            runKey.SetValue(ValueName, command);
            using var approvedKey = Registry.CurrentUser.OpenSubKey(StartupApprovedKey, writable: true);
            approvedKey?.DeleteValue(ValueName, throwOnMissingValue: false);
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
            using var runKey = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            runKey?.DeleteValue(ValueName, throwOnMissingValue: false);
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
            // Only the real app registers itself (not e.g. dotnet.exe during development), and it must not even create its key.
            if (!string.Equals(Path.GetFileName(Environment.ProcessPath), ExeName, StringComparison.OrdinalIgnoreCase))
                return;
            using var appKey = Registry.CurrentUser.CreateSubKey(AppSettingsKey);
            if (appKey.GetValue(InitializedValue) is not null)
                return;
            if (Enable())
                appKey.SetValue(InitializedValue, 1, RegistryValueKind.DWord); // retried next start if writing failed
        }
        catch (Exception ex)
        {
            AppLog.Write($"First-run start with Windows failed: {ex.Message}");
        }
    }
}
