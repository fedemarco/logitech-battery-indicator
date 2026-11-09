using System.Globalization;
using System.Text;
using LogitechBattery.Battery;

namespace LogitechBattery;

/// <summary>settings.ini: read once at startup, written only when the user changes a setting. Never throws.</summary>
public sealed class SettingsFile(string path)
{
    internal const string SupportedVersion = "1";
    internal const long MaxBytes = 64 * 1024;

    private const string VersionKey = "version";
    private const string LowPercentKey = "lowAlertPercent";
    private const string FullChargeAlertKey = "fullChargeAlert";
    private const int MaxQuotedCharacters = 40;

    private const string NewerVersionReason =
        "settings.ini has a version that this version of Logitech Battery does not know (a newer one?), so it does not change the file. The new setting applies until you exit.";
    private const string UnreadableReason =
        "settings.ini could not be read when the app started, so the app does not overwrite it. The new setting applies until you exit. Details are in log.txt.";
    private const string SaveFailedReason =
        "Couldn't save the settings. Close any program that has settings.ini open and try again. The new setting applies until you exit.";

    private static readonly string[] KnownKeys = [VersionKey, LowPercentKey, FullChargeAlertKey];

    // The file may hold values this version cannot read or does not know, so a save would lose them.
    private string? _doNotOverwriteReason;

    public AlertSettings Load()
    {
        try
        {
            if (!File.Exists(path))
                return AlertSettings.Default;
            if (new FileInfo(path).Length > MaxBytes)
                return KeepFileAndUseDefaults("settings.ini is larger than 64 KB; using the defaults");

            var parsed = Parse(File.ReadAllLines(path));
            if (parsed.IsNewerVersion)
            {
                // A newer version may add keys or allow other values, so its problems would only fill the log at every start.
                AppLog.Write("Settings: settings.ini has an unknown version; reading only the values this version knows");
                _doNotOverwriteReason = NewerVersionReason;
            }
            else
            {
                foreach (var problem in parsed.Problems)
                    AppLog.Write($"Settings: {problem}");
            }
            return parsed.Settings;
        }
        catch (Exception ex)
        {
            return KeepFileAndUseDefaults($"settings.ini could not be read: {ex.Message}");
        }
    }

    private AlertSettings KeepFileAndUseDefaults(string logMessage)
    {
        AppLog.Write($"Settings: {logMessage}");
        _doNotOverwriteReason = UnreadableReason;
        return AlertSettings.Default;
    }

    /// <summary>Returns null when saved, otherwise a reason the user can read.</summary>
    public string? Save(AlertSettings settings)
    {
        if (_doNotOverwriteReason is not null)
            return _doNotOverwriteReason;

        // A fixed temp name: a file left by a crash is replaced by the next save. Only one instance runs per user.
        var tempPath = path + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            WriteToDisk(tempPath, Format(settings));
            File.Move(tempPath, path, overwrite: true); // the file is the old one or the new one, never a part
            return null;
        }
        catch (Exception ex)
        {
            AppLog.Write($"Settings: save failed: {ex.Message}");
            return SaveFailedReason;
        }
        finally
        {
            DeleteQuietly(tempPath);
        }
    }

    // The flush matters: without it, a power loss just after the rename can leave an empty file on NTFS.
    private static void WriteToDisk(string filePath, string text)
    {
        using var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write);
        stream.Write(new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text));
        stream.Flush(flushToDisk: true);
    }

    private static void DeleteQuietly(string filePath)
    {
        try
        {
            File.Delete(filePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // a leftover temp file is replaced by the next save
        }
    }

    internal static string Format(AlertSettings settings) =>
        string.Join(Environment.NewLine,
            "# Logitech Battery settings. Exit the app before you edit this file: it reads the file only at startup.",
            $"# {LowPercentKey}: one of {ChoicesText}. {FullChargeAlertKey}: true or false.",
            $"{VersionKey}={SupportedVersion}",
            $"{LowPercentKey}={settings.LowPercent.ToString(CultureInfo.InvariantCulture)}",
            $"{FullChargeAlertKey}={(settings.FullChargeAlert ? "true" : "false")}",
            "");

    internal static ParsedSettings Parse(IEnumerable<string> lines)
    {
        var problems = new List<string>();
        var values = ReadKnownValues(lines, problems);

        var isNewerVersion = values.TryGetValue(VersionKey, out var version) && version != SupportedVersion;
        var settings = new AlertSettings(
            LowPercentFrom(values, problems),
            FullChargeAlertFrom(values, problems));
        return new ParsedSettings(settings, isNewerVersion, problems);
    }

    /// <summary>Key and value of each known key; the last line wins for a repeated key.</summary>
    private static Dictionary<string, string> ReadKnownValues(IEnumerable<string> lines, List<string> problems)
    {
        var values = new Dictionary<string, string>();
        var lineNumber = 0;
        foreach (var rawLine in lines)
        {
            lineNumber++;
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            var equalsAt = line.IndexOf('=');
            if (equalsAt < 0)
            {
                problems.Add($"line {lineNumber} has no '=': {Quote(line)}");
                continue;
            }

            var key = line[..equalsAt].Trim();
            if (!KnownKeys.Contains(key))
            {
                problems.Add($"unknown key '{Quote(key)}'");
                continue;
            }
            values[key] = line[(equalsAt + 1)..].Trim();
        }
        return values;
    }

    private static int LowPercentFrom(Dictionary<string, string> values, List<string> problems)
    {
        var fallback = AlertSettings.Default.LowPercent;
        if (!values.TryGetValue(LowPercentKey, out var text))
            return fallback;

        if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var percent) && AlertSettings.LowChoices.Contains(percent))
            return percent;
        problems.Add($"{LowPercentKey}={Quote(text)} is not one of {ChoicesText}; using {fallback}");
        return fallback;
    }

    private static bool FullChargeAlertFrom(Dictionary<string, string> values, List<string> problems)
    {
        var fallback = AlertSettings.Default.FullChargeAlert;
        if (!values.TryGetValue(FullChargeAlertKey, out var text))
            return fallback;

        if (bool.TryParse(text, out var enabled))
            return enabled;
        problems.Add($"{FullChargeAlertKey}={Quote(text)} is not true or false; using {fallback.ToString().ToLowerInvariant()}");
        return fallback;
    }

    private static string ChoicesText => string.Join(", ", AlertSettings.LowChoices);

    // A hand-edited file can hold any text, and the log should stay short.
    private static string Quote(string text) =>
        text.Length <= MaxQuotedCharacters ? text : text[..MaxQuotedCharacters] + "…";
}

internal sealed record ParsedSettings(AlertSettings Settings, bool IsNewerVersion, IReadOnlyList<string> Problems);
