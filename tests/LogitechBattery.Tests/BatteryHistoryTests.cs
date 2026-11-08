using LogitechBattery.Battery;

namespace LogitechBattery.Tests;

public sealed class BatteryHistoryTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 11, 4, 22, 50, 42, TimeSpan.Zero);

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "history-tests-" + Guid.NewGuid());
    private string FilePath => Path.Combine(_folder, "history.csv");

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    private static BatteryStatus Reading(
        int? percent = 82,
        ChargingState charging = ChargingState.Discharging,
        ConnectionState connection = ConnectionState.Connected,
        TimeSpan? after = null) =>
        TestData.Status(percent, charging, connection, at: Start + (after ?? TimeSpan.Zero));

    private string[] Lines() => File.Exists(FilePath) ? File.ReadAllLines(FilePath) : [];

    [Fact]
    public void Record_returns_true_when_it_wrote_a_line()
    {
        Assert.True(new BatteryHistory(FilePath).Record(Reading()));
    }

    [Fact]
    public void Record_returns_false_for_an_unchanged_reading()
    {
        var history = new BatteryHistory(FilePath);
        history.Record(Reading());

        Assert.False(history.Record(Reading(after: TimeSpan.FromMinutes(1))));
    }

    [Fact]
    public void Record_returns_false_without_throwing_when_the_write_fails()
    {
        Directory.CreateDirectory(_folder);
        var blocker = Path.Combine(_folder, "blocker");
        File.WriteAllText(blocker, "a file, not a folder");

        Assert.False(new BatteryHistory(Path.Combine(blocker, "history.csv")).Record(Reading()));
    }

    [Fact]
    public void First_reading_writes_a_line()
    {
        new BatteryHistory(FilePath).Record(Reading());

        Assert.Single(Lines());
    }

    [Fact]
    public void Line_has_the_exact_format()
    {
        new BatteryHistory(FilePath).Record(Reading());

        Assert.Equal("2026-11-04T22:50:42Z,82,Discharging,Connected", Lines()[0]);
    }

    [Fact]
    public void Time_is_written_in_utc()
    {
        var local = new BatteryStatus(ConnectionState.Connected, 82, ChargingState.Discharging, Start.ToOffset(TimeSpan.FromHours(2)));

        new BatteryHistory(FilePath).Record(local);

        Assert.StartsWith("2026-11-04T22:50:42Z,", Lines()[0]);
    }

    [Fact]
    public void Unchanged_reading_within_15_minutes_writes_nothing()
    {
        var history = new BatteryHistory(FilePath);
        history.Record(Reading());

        history.Record(Reading(after: TimeSpan.FromMinutes(14)));

        Assert.Single(Lines());
    }

    [Fact]
    public void Unchanged_reading_after_15_minutes_writes_a_heartbeat()
    {
        var history = new BatteryHistory(FilePath);
        history.Record(Reading());

        history.Record(Reading(after: TimeSpan.FromMinutes(15)));

        Assert.Equal(2, Lines().Length);
    }

    [Fact]
    public void Heartbeat_is_measured_from_the_last_written_record()
    {
        var history = new BatteryHistory(FilePath);
        history.Record(Reading());
        history.Record(Reading(after: TimeSpan.FromMinutes(10)));

        history.Record(Reading(after: TimeSpan.FromMinutes(20)));

        Assert.Equal(2, Lines().Length);
    }

    [Fact]
    public void Changed_percent_writes_a_line()
    {
        var history = new BatteryHistory(FilePath);
        history.Record(Reading(percent: 82));

        history.Record(Reading(percent: 81));

        Assert.Equal(2, Lines().Length);
    }

    [Fact]
    public void Changed_charging_state_writes_a_line()
    {
        var history = new BatteryHistory(FilePath);
        history.Record(Reading());

        history.Record(Reading(charging: ChargingState.Charging));

        Assert.Equal(2, Lines().Length);
    }

    [Fact]
    public void Changed_connection_writes_a_line()
    {
        var history = new BatteryHistory(FilePath);
        history.Record(Reading());

        history.Record(Reading(connection: ConnectionState.Asleep));

        Assert.Equal(2, Lines().Length);
    }

    [Fact]
    public void Unknown_percent_is_written_as_an_empty_field_and_read_back_as_null()
    {
        new BatteryHistory(FilePath).Record(Reading(percent: null, connection: ConnectionState.Asleep));

        Assert.Equal("2026-11-04T22:50:42Z,,Discharging,Asleep", Lines()[0]);
        var record = Assert.Single(new BatteryHistory(FilePath).ReadSince(DateTimeOffset.MinValue));
        Assert.Null(record.Percent);
    }

    [Fact]
    public void Restart_does_not_duplicate_an_unchanged_state()
    {
        new BatteryHistory(FilePath).Record(Reading());

        new BatteryHistory(FilePath).Record(Reading(after: TimeSpan.FromMinutes(1)));

        Assert.Single(Lines());
    }

    [Fact]
    public void ReadSince_returns_records_at_or_after_the_given_time_in_file_order()
    {
        var history = new BatteryHistory(FilePath);
        history.Record(Reading(percent: 80));
        history.Record(Reading(percent: 79, after: TimeSpan.FromHours(1)));
        history.Record(Reading(percent: 78, after: TimeSpan.FromHours(2)));

        var records = history.ReadSince(Start + TimeSpan.FromHours(1));

        Assert.Equal([79, 78], records.Select(record => record.Percent));
    }

    [Fact]
    public void ReadSince_skips_lines_that_do_not_parse()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllLines(FilePath,
        [
            "2026-11-04T22:50:42Z,80,Discharging,Connected",
            "garbage",
            "2026-11-04T22:51:00Z,abc,Discharging,Connected",
            "2026-11-04T22:52:00Z,79,Sideways,Connected",
            "not-a-time,79,Discharging,Connected",
            "2026-11-04T22:53:00Z,78,Discharging,Connected",
        ]);

        var records = new BatteryHistory(FilePath).ReadSince(DateTimeOffset.MinValue);

        Assert.Equal([80, 78], records.Select(record => record.Percent));
    }

    [Fact]
    public void Prune_drops_records_older_than_the_age_before_the_newest_record()
    {
        var history = new BatteryHistory(FilePath);
        history.Record(Reading(percent: 80));
        history.Record(Reading(percent: 75, after: TimeSpan.FromDays(6)));
        history.Record(Reading(percent: 70, after: TimeSpan.FromDays(10)));

        history.PruneOlderThan(TimeSpan.FromDays(3), Start + TimeSpan.FromDays(10));

        Assert.Equal([70], history.ReadSince(DateTimeOffset.MinValue).Select(record => record.Percent));
    }

    [Fact]
    public void Prune_leaves_no_temporary_file_behind()
    {
        var history = new BatteryHistory(FilePath);
        history.Record(Reading(percent: 80));
        history.Record(Reading(percent: 70, after: TimeSpan.FromDays(10)));

        history.PruneOlderThan(TimeSpan.FromDays(3), Start + TimeSpan.FromDays(10));

        Assert.Equal([FilePath], Directory.GetFiles(_folder));
    }

    [Fact]
    public void Prune_keeps_a_record_exactly_at_the_cutoff()
    {
        var history = new BatteryHistory(FilePath);
        history.Record(Reading(percent: 80));
        history.Record(Reading(percent: 70, after: TimeSpan.FromDays(5)));

        history.PruneOlderThan(TimeSpan.FromDays(5), Start + TimeSpan.FromDays(5));

        Assert.Equal([80, 70], history.ReadSince(DateTimeOffset.MinValue).Select(record => record.Percent));
    }

    [Fact]
    public void Prune_ignores_a_record_dated_after_now_when_choosing_the_cutoff()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllLines(FilePath,
        [
            "2026-11-04T10:00:00Z,80,Discharging,Connected",
            "2030-01-01T10:00:00Z,79,Discharging,Connected",
            "2026-11-05T10:00:00Z,78,Discharging,Connected",
        ]);
        var history = new BatteryHistory(FilePath);

        history.PruneOlderThan(TimeSpan.FromDays(3), new DateTimeOffset(2026, 11, 6, 0, 0, 0, TimeSpan.Zero));

        Assert.Equal([80, 79, 78], history.ReadSince(DateTimeOffset.MinValue).Select(record => record.Percent));
    }

    [Fact]
    public void Prune_drops_nothing_when_every_record_is_dated_after_now()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllLines(FilePath, ["2030-01-01T10:00:00Z,79,Discharging,Connected"]);
        var history = new BatteryHistory(FilePath);

        history.PruneOlderThan(TimeSpan.FromDays(3), Start);

        Assert.Single(history.ReadSince(DateTimeOffset.MinValue));
    }

    [Fact]
    public void Prune_leaves_the_file_content_unchanged_when_nothing_is_old()
    {
        var history = new BatteryHistory(FilePath);
        history.Record(Reading(percent: 80));
        history.Record(Reading(percent: 79, after: TimeSpan.FromDays(1)));
        var before = File.ReadAllText(FilePath);

        history.PruneOlderThan(TimeSpan.FromDays(365), Start + TimeSpan.FromDays(1));

        Assert.Equal(before, File.ReadAllText(FilePath));
    }

    [Fact]
    public void Prune_on_a_missing_file_does_nothing()
    {
        new BatteryHistory(FilePath).PruneOlderThan(TimeSpan.FromDays(365), Start);

        Assert.False(File.Exists(FilePath));
    }

    [Fact]
    public void Record_after_a_partial_last_line_writes_the_new_record_on_its_own_line()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(FilePath, "2026-11-04T22:40:00Z,90,Disch");

        new BatteryHistory(FilePath).Record(Reading());

        Assert.Equal("2026-11-04T22:50:42Z,82,Discharging,Connected", Lines()[1]);
    }

    [Fact]
    public void Clock_moved_back_still_writes_a_heartbeat()
    {
        var history = new BatteryHistory(FilePath);
        history.Record(Reading(after: TimeSpan.FromHours(1)));

        history.Record(Reading());

        Assert.Equal(2, Lines().Length);
    }

    [Fact]
    public void Asleep_reading_is_written_with_an_empty_percent()
    {
        new BatteryHistory(FilePath).Record(Reading(percent: null, charging: ChargingState.Unknown, connection: ConnectionState.Asleep));

        Assert.Equal("2026-11-04T22:50:42Z,,Unknown,Asleep", Lines()[0]);
    }

    [Theory]
    [InlineData("2026-11-04T22:50:42Z,101,Discharging,Connected")] // above 100
    [InlineData("2026-11-04T22:50:42Z,-1,Discharging,Connected")]  // negative
    public void ReadSince_skips_a_percent_outside_0_to_100(string line) => AssertLineIsSkipped(line);

    [Theory]
    [InlineData("2026-11-04T22:50:42Z,82,7,Connected")]            // number that is no ChargingState
    [InlineData("2026-11-04T22:50:42Z,82,Discharging,9")]          // number that is no ConnectionState
    [InlineData("2026-11-04T22:50:42Z,82,Discharging,1")]          // number that is a ConnectionState (Connected): names only
    [InlineData("2026-11-04T22:50:42Z,82, 1,Connected")]           // leading space before a number
    [InlineData("2026-11-04T22:50:42Z,82,+1,Connected")]           // sign before a number
    [InlineData("2026-11-04T22:50:42Z,82, Discharging,Connected")] // leading space before the name
    [InlineData("2026-11-04T22:50:42Z,82,discharging,Connected")]  // wrong case
    public void ReadSince_skips_a_state_that_is_not_an_exact_enum_name(string line) => AssertLineIsSkipped(line);

    private void AssertLineIsSkipped(string line)
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllLines(FilePath, [line]);

        Assert.Empty(new BatteryHistory(FilePath).ReadSince(DateTimeOffset.MinValue));
    }

    [Fact]
    public void Clear_deletes_the_file_and_returns_true()
    {
        var history = new BatteryHistory(FilePath);
        history.Record(Reading());

        Assert.True(history.Clear());
        Assert.False(File.Exists(FilePath));
    }

    [Fact]
    public void Clear_on_a_missing_file_returns_true()
    {
        Assert.True(new BatteryHistory(FilePath).Clear());
    }

    [Fact]
    public void Clear_returns_false_when_the_file_cannot_be_deleted()
    {
        Directory.CreateDirectory(FilePath); // File.Delete cannot remove a directory

        Assert.False(new BatteryHistory(FilePath).Clear());
    }

    [Fact]
    public void Reading_after_Clear_is_written_again()
    {
        var history = new BatteryHistory(FilePath);
        history.Record(Reading());
        history.Clear();

        history.Record(Reading());

        Assert.Single(Lines());
    }
}
