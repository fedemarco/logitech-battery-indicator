using System.Globalization;

namespace LogitechBattery;

/// <summary>Tiny local log file: %LOCALAPPDATA%\LogitechBattery\log.txt. Never throws.</summary>
internal static class AppLog
{
    private const long MaxBytes = 256 * 1024;
    private static readonly object Gate = new();
    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LogitechBattery");

    public static void Write(string message)
    {
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                var file = Path.Combine(Dir, "log.txt");
                if (File.Exists(file) && new FileInfo(file).Length > MaxBytes)
                    File.Move(file, Path.Combine(Dir, "log.old.txt"), overwrite: true);
                File.AppendAllText(file, $"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}  {message}{Environment.NewLine}");
            }
            catch { /* logging must never break the app */ }
        }
    }
}
