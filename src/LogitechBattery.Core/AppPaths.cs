namespace LogitechBattery;

public static class AppPaths
{
    /// <summary>
    /// %LOCALAPPDATA%\LogitechBattery: log, probe report and the installed exe live here.
    /// Only the test project can change it (so tests never write to the user's real folder).
    /// </summary>
    public static string DataDirectory { get; internal set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LogitechBattery");
}
