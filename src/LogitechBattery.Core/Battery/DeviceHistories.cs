namespace LogitechBattery.Battery;

/// <summary>
/// One <see cref="BatteryHistory"/> file per device in <c>history\&lt;DeviceId&gt;.csv</c>, and the one-time move of the
/// single <c>history.csv</c> of earlier versions. Nothing here throws: failures are logged and the history stays as it is.
/// </summary>
public sealed class DeviceHistories(string dataDirectory)
{
    /// <summary>Earlier versions supported only this mouse, so the old single file holds its readings.</summary>
    internal const string LegacyDeviceName = "PRO X2 SUPERSTRIKE";

    private const string FolderName = "history";
    private const string LegacyFileName = "history.csv";
    private const string FileExtension = ".csv";
    private const char FieldSeparator = ',';

    private readonly Dictionary<DeviceId, BatteryHistory> _histories = [];

    private string Folder => Path.Combine(dataDirectory, FolderName);
    private string LegacyPath => Path.Combine(dataDirectory, LegacyFileName);

    private string PathOf(DeviceId id) => Path.Combine(Folder, id + FileExtension);

    /// <summary>The first call for a device moves the old file in when it applies and prunes records older than the retention.</summary>
    public BatteryHistory For(DeviceInfo device, DateTimeOffset now)
    {
        if (_histories.TryGetValue(device.Id, out var known))
            return known;

        if (device.Name == LegacyDeviceName && File.Exists(LegacyPath))
            MigrateLegacyFile(PathOf(device.Id));

        var history = new BatteryHistory(PathOf(device.Id));
        history.PruneOlderThan(BatteryHistory.Retention, now);
        _histories[device.Id] = history;
        return history;
    }

    /// <summary>Returns true when no history file is left; the folder stays.</summary>
    public bool ClearAll()
    {
        var cleared = _histories.Values.Select(history => history.Clear()).ToList().All(done => done);
        return DeleteUnknownFiles() && cleared;
    }

    private bool DeleteUnknownFiles()
    {
        try
        {
            if (Directory.Exists(Folder))
            {
                foreach (var file in Directory.GetFiles(Folder, "*" + FileExtension))
                    File.Delete(file);
            }
            if (File.Exists(LegacyPath))
                File.Delete(LegacyPath);
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Write($"Clearing the history files failed: {ex.Message}");
            return false;
        }
    }

    // Every crash state is valid: the next start merges again, and the merge drops duplicate lines.
    private void MigrateLegacyFile(string targetPath)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            if (File.Exists(targetPath))
                MergeIntoTarget(targetPath);
            else
                File.Move(LegacyPath, targetPath);
        }
        catch (Exception ex)
        {
            AppLog.Write($"Moving the old history file failed, trying again at the next start: {ex.Message}");
        }
    }

    // Unparsable lines are kept too: the merge must not lose anything, and pruning drops them later as it does today.
    private void MergeIntoTarget(string targetPath)
    {
        var lines = File.ReadLines(targetPath).Concat(File.ReadLines(LegacyPath))
            .Where(line => line.Length > 0)
            .Distinct()
            .OrderBy(TimestampOf, StringComparer.Ordinal) // ISO UTC sorts as text; the sort is stable
            .ToList();

        var tempPath = targetPath + ".tmp"; // a leftover from a crash is replaced
        using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            using var writer = new StreamWriter(stream);
            foreach (var line in lines)
                writer.WriteLine(line);
            writer.Flush();
            stream.Flush(flushToDisk: true);
        }
        File.Move(tempPath, targetPath, overwrite: true);
        File.Delete(LegacyPath); // last, so a crash before this point only repeats the merge
    }

    private static string TimestampOf(string line) => line.Split(FieldSeparator)[0];
}
