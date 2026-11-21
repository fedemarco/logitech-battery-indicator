using System.Globalization;
using System.Text;
using LogitechBattery.History;

namespace LogitechBattery.Logging;

public enum LogLevel { Info, Warn, Error }

/// <summary>Builds one logfmt-style log line: "time LEVEL message key=value key="quoted value"". Always a single physical line.</summary>
internal static class LogLine
{
    private const string TimeFormat = "yyyy-MM-ddTHH:mm:ss.fffzzz";
    private const int LevelWidth = 5;

    public static string Format(DateTimeOffset time, LogLevel level, string message, IReadOnlyList<(string Key, object? Value)> fields)
    {
        var line = new StringBuilder();
        line.Append(time.ToString(TimeFormat, CultureInfo.InvariantCulture));
        line.Append(' ').Append(LevelText(level).PadRight(LevelWidth)).Append(' ').Append(message);
        foreach (var (key, value) in fields)
            line.Append(' ').Append(key).Append('=').Append(FormatValue(value));
        return line.ToString();
    }

    private static string LevelText(LogLevel level) => level.ToString().ToUpperInvariant();

    private static string FormatValue(object? value)
    {
        var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
        return NeedsQuotes(text) ? "\"" + Escape(text) + "\"" : text;
    }

    private static bool NeedsQuotes(string text) =>
        text.Length == 0 || text.Any(character => character is ' ' or '=' or '"' or '\\' || char.IsControl(character));

    private static string Escape(string text)
    {
        var escaped = new StringBuilder();
        foreach (var character in text)
        {
            switch (character)
            {
                case '\\': escaped.Append(@"\\"); break;
                case '"': escaped.Append("\\\""); break;
                case '\n': escaped.Append(@"\n"); break;
                case '\r': escaped.Append(@"\r"); break;
                case '\t': escaped.Append(@"\t"); break;
                case var control when char.IsControl(control): escaped.Append($"\\u{(int)control:X4}"); break;
                default: escaped.Append(character); break;
            }
        }
        return escaped.ToString();
    }
}
