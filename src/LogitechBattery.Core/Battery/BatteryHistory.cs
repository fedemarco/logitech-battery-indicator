using System.Globalization;

namespace LogitechBattery.Battery;

/// <summary>
/// Append-only CSV of battery readings, one line per change plus a heartbeat.
/// History is not critical: IO errors are logged and swallowed.
/// </summary>
public sealed class BatteryHistory(string path)
{
    public static readonly TimeSpan HeartbeatInterval = TimeSpan.FromMinutes(15);

    /// <summary>How long history is kept: older records are pruned at startup, and the panel looks this far back for the last charge.</summary>
    public static readonly TimeSpan Retention = TimeSpan.FromDays(365);
    private const string TimeFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";

    private HistoryRecord? _last;
    private bool _lastLoaded;

    /// <summary>Returns true when a new line was written.</summary>
    public bool Record(BatteryStatus status) => Guard(nameof(Record), fallback: false, () =>
    {
        var record = new HistoryRecord(status.ReadAt.ToUniversalTime(), status.Percent, status.Charging, status.Connection);
        if (!NeedsWriting(record))
            return false;

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var separator = EndsWithPartialLine() ? Environment.NewLine : "";
        File.AppendAllText(path, separator + Format(record) + Environment.NewLine);
        _last = record;
        return true;
    });

    public IReadOnlyList<HistoryRecord> ReadSince(DateTimeOffset since) =>
        Guard<IReadOnlyList<HistoryRecord>>(nameof(ReadSince), fallback: [], () =>
            ReadAll().Where(record => record.Time >= since).ToList());

    /// <summary>
    /// Drops records older than <paramref name="age"/> before the newest record that is not after <paramref name="now"/>,
    /// so a record dated in the future (wrong clock) cannot wipe the history.
    /// </summary>
    public void PruneOlderThan(TimeSpan age, DateTimeOffset now) => Guard(nameof(PruneOlderThan), () =>
    {
        var all = ReadAll();
        var pastRecords = all.Where(record => record.Time <= now).ToList();
        if (pastRecords.Count == 0)
            return;

        var cutoff = pastRecords.Max(record => record.Time) - age;
        var kept = all.Where(record => record.Time >= cutoff).ToList();
        if (kept.Count == all.Count)
            return;

        var tempPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, Path.GetRandomFileName());
        try
        {
            File.WriteAllLines(tempPath, kept.Select(Format));
            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            File.Delete(tempPath); // no-op after a successful move
        }
    });

    /// <summary>Returns true when the file is gone afterwards (deleted, or there was none).</summary>
    public bool Clear() => Guard(nameof(Clear), fallback: false, () =>
    {
        try
        {
            File.Delete(path);
        }
        catch (DirectoryNotFoundException)
        {
            // the data folder does not exist yet, so there is no file either
        }
        _last = null;
        _lastLoaded = true; // the file is gone, nothing to load
        return true;
    });

    private bool NeedsWriting(HistoryRecord record)
    {
        if (!_lastLoaded)
        {
            _last = ReadAll().LastOrDefault();
            _lastLoaded = true;
        }
        return _last is null
            || record.Percent != _last.Percent
            || record.Charging != _last.Charging
            || record.Connection != _last.Connection
            || IsHeartbeatDueOrClockMovedBack(record.Time - _last.Time);
    }

    // A negative delta means the clock moved back; waiting for it to catch up would leave a long gap.
    private static bool IsHeartbeatDueOrClockMovedBack(TimeSpan sinceLastRecord) =>
        sinceLastRecord < TimeSpan.Zero || sinceLastRecord >= HeartbeatInterval;

    private bool EndsWithPartialLine()
    {
        if (!File.Exists(path))
            return false;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (stream.Length == 0)
            return false;
        stream.Seek(-1, SeekOrigin.End);
        return stream.ReadByte() != '\n';
    }

    private List<HistoryRecord> ReadAll() =>
        File.Exists(path)
            ? File.ReadLines(path).Select(TryParse).OfType<HistoryRecord>().ToList()
            : [];

    // The CSV stores the enum names, so renaming a ChargingState or ConnectionState member orphans existing history files.
    private static string Format(HistoryRecord record) =>
        string.Join(',',
            record.Time.UtcDateTime.ToString(TimeFormat, CultureInfo.InvariantCulture),
            record.Percent?.ToString(CultureInfo.InvariantCulture) ?? "",
            record.Charging,
            record.Connection);

    private static HistoryRecord? TryParse(string line)
    {
        var fields = line.Split(',');
        if (fields.Length != 4
            || !DateTimeOffset.TryParseExact(fields[0], TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var time)
            || !TryParseDefinedEnum(fields[2], out ChargingState charging)
            || !TryParseDefinedEnum(fields[3], out ConnectionState connection))
            return null;

        if (fields[1].Length == 0)
            return new HistoryRecord(time, null, charging, connection);
        return int.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out var percent) && percent <= BatteryStatus.FullPercent
            ? new HistoryRecord(time, percent, charging, connection)
            : null;
    }

    private static bool TryParseDefinedEnum<T>(string text, out T value) where T : struct, Enum
    {
        value = default;
        if (!Enum.IsDefined(typeof(T), text))
            return false;
        value = Enum.Parse<T>(text);
        return true;
    }

    private static void Guard(string operation, Action action) =>
        Guard(operation, fallback: false, () =>
        {
            action();
            return false;
        });

    /// <summary>Runs one history operation; any failure is logged and gives <paramref name="fallback"/>.</summary>
    private static T Guard<T>(string operation, T fallback, Func<T> action)
    {
        try
        {
            return action();
        }
        catch (Exception ex)
        {
            AppLog.Write($"Battery history {operation} failed: {ex.Message}");
            return fallback;
        }
    }
}
