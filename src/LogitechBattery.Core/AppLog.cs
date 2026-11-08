using System.Globalization;

namespace LogitechBattery;

/// <summary>Tiny local log file: %LOCALAPPDATA%\LogitechBattery\log.txt. Never throws.</summary>
public static class AppLog
{
    private const string FileName = "log.txt";
    private const string RotatedFileName = "log.old.txt";
    private const long RotateAtBytes = 256 * 1024;
    private const long StopWritingAtBytes = 1024 * 1024; // only reached when rotation keeps failing
    private static readonly object Gate = new();

    public static void Write(string message)
    {
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(AppPaths.DataDirectory);
                var file = Path.Combine(AppPaths.DataDirectory, FileName);
                if (!RotateIfNeeded(file))
                    return;
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                File.AppendAllText(file, $"{timestamp}  {message}{Environment.NewLine}");
            }
            catch { /* logging must never break the app */ }
        }
    }

    /// <summary>Moves a full log aside. Returns false when writing must stop because rotation is blocked and the file keeps growing.</summary>
    private static bool RotateIfNeeded(string file)
    {
        if (!File.Exists(file) || new FileInfo(file).Length <= RotateAtBytes)
            return true;
        try
        {
            File.Move(file, Path.Combine(AppPaths.DataDirectory, RotatedFileName), overwrite: true);
            return true;
        }
        catch
        {
            return new FileInfo(file).Length <= StopWritingAtBytes;
        }
    }
}
