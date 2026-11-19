using System.Globalization;
using System.Text.RegularExpressions;

namespace LogitechBattery;

/// <summary>Tiny local log file: %LOCALAPPDATA%\LogitechBattery\log.txt. Never throws.</summary>
public static class AppLog
{
    private const string FileName = "log.txt";
    private const string RotatedFileName = "log.old.txt";
    internal const long RotateAtBytes = 256 * 1024;
    internal const long StopWritingAtBytes = 1024 * 1024; // only reached when rotation keeps failing
    private const string ProfilePlaceholder = "%USERPROFILE%";
    private static readonly object Gate = new();

    public static void Info(string message, params (string Key, object? Value)[] fields) =>
        Write(AppPaths.DataDirectory, LogLevel.Info, message, fields);

    public static void Warn(string message, params (string Key, object? Value)[] fields) =>
        Write(AppPaths.DataDirectory, LogLevel.Warn, message, fields);

    public static void Error(string message, params (string Key, object? Value)[] fields) =>
        Write(AppPaths.DataDirectory, LogLevel.Error, message, fields);

    /// <summary>Fields for a handled failure: the exception type and message.</summary>
    public static (string Key, object? Value)[] Failure(Exception exception) =>
        [("error", exception.GetType().Name), ("message", exception.Message)];

    /// <summary>Fields for an unhandled failure: type, message and the full stack trace in one escaped value.</summary>
    public static (string Key, object? Value)[] FailureWithStack(Exception exception) =>
        [.. Failure(exception), ("stack", exception.ToString())];

    internal static void Write(string directory, LogLevel level, string message, params (string Key, object? Value)[] fields)
    {
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(directory);
                var file = Path.Combine(directory, FileName);
                if (!RotateIfNeeded(file, directory))
                    return;
                var line = LogLine.Format(DateTimeOffset.Now, level, Redact(message), Redacted(fields));
                File.AppendAllText(file, line + Environment.NewLine);
            }
            catch { /* logging must never break the app */ }
        }
    }

    // Redacted before formatting: escaping doubles the backslashes of a Windows path, and then the profile folder no longer matches.
    private static List<(string Key, object? Value)> Redacted((string Key, object? Value)[] fields) =>
        fields.Select(field => (field.Key, (object?)Redact(Convert.ToString(field.Value, CultureInfo.InvariantCulture) ?? ""))).ToList();

    /// <summary>
    /// Replaces the user profile folder with %USERPROFILE%, so a log or report that is shared does not show the Windows user name.
    /// Only the whole folder path is replaced, never the bare name.
    /// </summary>
    public static string Redact(string text) => Redact(text, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    internal static string Redact(string text, string profileFolder)
    {
        var folder = profileFolder.TrimEnd('\\', '/');
        var isBareDriveOrEmpty = folder.IndexOfAny(['\\', '/']) < 0; // "C:" would hit every drive letter in the text
        if (isBareDriveOrEmpty)
            return text;

        // The lookahead keeps a longer folder name that starts the same (C:\Users\anna vs C:\Users\annabel) untouched.
        return Regex.Replace(text, Regex.Escape(folder) + @"(?!\w)", ProfilePlaceholder, RegexOptions.IgnoreCase);
    }

    /// <summary>Moves a full log aside. Returns false when writing must stop because rotation is blocked and the file keeps growing.</summary>
    private static bool RotateIfNeeded(string file, string directory)
    {
        if (!File.Exists(file) || new FileInfo(file).Length <= RotateAtBytes)
            return true;
        try
        {
            File.Move(file, Path.Combine(directory, RotatedFileName), overwrite: true);
            return true;
        }
        catch
        {
            return new FileInfo(file).Length <= StopWritingAtBytes;
        }
    }
}
