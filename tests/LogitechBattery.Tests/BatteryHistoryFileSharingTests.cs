using LogitechBattery.Battery;

namespace LogitechBattery.Tests;

/// <summary>Another program (a viewer, a sync tool, an antivirus scan) can hold the history file open; the history must neither throw nor lose data.</summary>
public sealed class BatteryHistoryFileSharingTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);

    private readonly TempFolder _folder = new();

    public BatteryHistoryFileSharingTests()
    {
        Directory.CreateDirectory(_folder.Path);
        File.WriteAllLines(HistoryPath,
        [
            "2026-09-01T10:00:00Z,80,Discharging,Connected", // more than 30 days old
            "2026-11-05T10:00:00Z,70,Discharging,Connected",
        ]);
    }

    public void Dispose() => _folder.Dispose();

    private string HistoryPath => _folder.FileNamed("history.csv");

    private BatteryHistory History => new(HistoryPath);

    private FileStream HoldExclusively() => new(HistoryPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

    private FileStream HoldLikeAReader() => new(HistoryPath, FileMode.Open, FileAccess.Read, FileShare.Read);

    [Fact]
    public void ReadSince_returns_nothing_while_another_program_locks_the_file()
    {
        using var lockedFile = HoldExclusively();

        Assert.Empty(History.ReadSince(DateTimeOffset.MinValue));
    }

    [Fact]
    public void Record_returns_false_while_the_file_is_locked_and_writes_once_it_is_free()
    {
        var history = History;
        var reading = TestData.Status(percent: 40, at: Now);

        using (HoldExclusively())
            Assert.False(history.Record(reading));

        Assert.True(history.Record(reading));
    }

    [WindowsFact]
    public void Clear_returns_false_and_keeps_the_history_while_a_reader_has_the_file_open()
    {
        var history = History;
        using var reader = HoldLikeAReader();

        Assert.False(history.Clear());
        reader.Dispose();

        Assert.Equal(2, history.ReadSince(DateTimeOffset.MinValue).Count);
    }

    [WindowsFact]
    public void Prune_blocked_by_a_reader_keeps_the_file_and_leaves_no_temporary_file()
    {
        using var reader = HoldLikeAReader();

        History.PruneOlderThan(TimeSpan.FromDays(30), Now);
        reader.Dispose();

        Assert.Equal(2, File.ReadAllLines(HistoryPath).Length);
        Assert.Equal([HistoryPath], Directory.GetFiles(_folder.Path));
    }
}
