namespace LogitechBattery;

internal static class AppPaths
{
    /// <summary>%LOCALAPPDATA%\LogitechBattery: log, probe report and the installed exe live here.</summary>
    public static string DataDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LogitechBattery");
}
