namespace LogitechBattery;

/// <summary>Whether Windows lets a "Start with Windows" entry run, as the Task Manager "Startup apps" toggle stores it.</summary>
public static class StartupApproval
{
    /// <summary>
    /// The toggle stores a binary value whose first byte is odd when the entry is disabled.
    /// No value, or one that is not a non-empty byte array, means enabled.
    /// </summary>
    public static bool IsApproved(object? registryValue) =>
        registryValue is not byte[] { Length: > 0 } flags || (flags[0] & 1) == 0;
}
