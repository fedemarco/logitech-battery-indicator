namespace LogitechBattery.Tests;

/// <summary>A fact about Windows file sharing: Unix lets a file be renamed or deleted while it is open.</summary>
public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
            Skip = "Windows only";
    }
}
