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
            return IsRunEntryForThisExe() && IsApprovedInTaskManager();
        }
        catch (Exception ex)
        {
            AppLog.Write($"Reading start with Windows failed: {ex.Message}");
            return false;
        }
    }

    private static bool IsRunEntryForThisExe()
    {
        using var runKey = Registry.CurrentUser.OpenSubKey(RunKey);
        return runKey?.GetValue(ValueName) is string value
            && Command is { } command
            && string.Equals(value, command, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsApprovedInTaskManager()
    {
        using var approvedKey = Registry.CurrentUser.OpenSubKey(StartupApprovedKey);
        return StartupApproval.IsApproved(approvedKey?.GetValue(ValueName));
    }

    /// <summary>Writes the Run entry and clears a Task Manager "disabled" flag. Returns false when start with Windows is still off.</summary>
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
            ClearTaskManagerDisabledFlag();
            return IsEnabled();
        }
        catch (Exception ex)
        {
            AppLog.Write($"Enabling start with Windows failed: {ex.Message}");
            return false;
        }
    }

    // A failure is only logged: when no "disabled" flag exists, start-up works anyway. Enable() checks the real result.
    private static void ClearTaskManagerDisabledFlag()
    {
        try
        {
            using var approvedKey = Registry.CurrentUser.OpenSubKey(StartupApprovedKey, writable: true);
            approvedKey?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch (Exception ex)
        {
            AppLog.Write($"Clearing the Task Manager startup flag failed: {ex.Message}");
        }
    }

    /// <summary>Removes the Run entry. Returns false on failure.</summary>
    public static bool Disable()
    {
        try
        {
            using var runKey = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            runKey?.DeleteValue(ValueName, throwOnMissingValue: false);
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Write($"Disabling start with Windows failed: {ex.Message}");
            return false;
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
