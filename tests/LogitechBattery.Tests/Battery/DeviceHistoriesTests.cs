using LogitechBattery.Battery;

namespace LogitechBattery.Tests;

public sealed class DeviceHistoriesTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly DeviceInfo Mouse = new(DeviceId.Parse("62C31634"), DeviceInfo.ProX2SuperstrikeName);
    private static readonly DeviceInfo Keyboard = new(DeviceId.Parse("A1B2C3D4"), "MX Keys");

    private const string Early = "2026-11-05T08:00:00Z,80,Discharging,Connected";
    private const string Middle = "2026-11-05T09:00:00Z,75,Discharging,Connected";
    private const string Late = "2026-11-05T10:00:00Z,70,Discharging,Connected";

    private readonly TempFolder _data = new();

    public void Dispose() => _data.Dispose();

    private string LegacyPath => _data.FileNamed("history.csv");
    private string MouseFile => Path.Combine(_data.Path, "history", "62C31634.csv");
    private string HistoryFolder => Path.Combine(_data.Path, "history");

    private DeviceHistories NewSession() => new(_data.Path);

    private void WriteLegacy(params string[] lines)
    {
        Directory.CreateDirectory(_data.Path);
        File.WriteAllLines(LegacyPath, lines);
    }

    private void WriteMouseFile(params string[] lines)
    {
        Directory.CreateDirectory(HistoryFolder);
        File.WriteAllLines(MouseFile, lines);
    }

    [Fact]
    public void For_returns_the_same_instance_for_the_same_device_and_another_for_another_device()
    {
        var histories = NewSession();

        Assert.Same(histories.For(Mouse, Now), histories.For(Mouse, Now));
        Assert.NotSame(histories.For(Mouse, Now), histories.For(Keyboard, Now));
    }

    [Fact]
    public void Record_writes_to_a_file_named_after_the_device()
    {
        NewSession().For(Keyboard, Now).Record(TestData.Status(percent: 50, at: Now));

        Assert.True(File.Exists(Path.Combine(HistoryFolder, "A1B2C3D4.csv")));
    }

    [Fact]
    public void The_old_file_moves_to_the_mouse_history_when_there_is_none_yet()
    {
        WriteLegacy(Early, Late);

        NewSession().For(Mouse, Now);

        Assert.Equal([Early, Late], File.ReadAllLines(MouseFile));
        Assert.False(File.Exists(LegacyPath));
    }

    [Fact]
    public void The_old_file_stays_for_a_device_with_another_name()
    {
        WriteLegacy(Early);

        NewSession().For(Keyboard, Now);

        Assert.True(File.Exists(LegacyPath));
        Assert.False(File.Exists(Path.Combine(HistoryFolder, "A1B2C3D4.csv")));
    }

    [Fact]
    public void The_old_file_goes_to_the_first_device_with_the_mouse_name_only()
    {
        WriteLegacy(Early);
        var secondMouse = new DeviceInfo(DeviceId.Parse("0BADC0DE"), DeviceInfo.ProX2SuperstrikeName);
        var histories = NewSession();

        histories.For(Mouse, Now);
        histories.For(secondMouse, Now);

        Assert.False(File.Exists(Path.Combine(HistoryFolder, "0BADC0DE.csv")));
    }

    [Fact]
    public void The_placeholder_for_a_missing_device_has_no_history_and_leaves_the_old_file_alone()
    {
        WriteLegacy(Early);
        var histories = NewSession();

        Assert.Throws<ArgumentException>(() => histories.For(DeviceInfo.NoDeviceYet, Now));

        Assert.True(File.Exists(LegacyPath));
        Assert.False(Directory.Exists(HistoryFolder));
    }

    [Fact]
    public void Merging_removes_duplicate_lines_and_orders_by_time()
    {
        WriteLegacy(Late, Early, Middle);
        WriteMouseFile(Early, Middle);

        NewSession().For(Mouse, Now);

        Assert.Equal([Early, Middle, Late], File.ReadAllLines(MouseFile));
        Assert.False(File.Exists(LegacyPath));
    }

    [Fact]
    public void Merging_again_after_a_crash_before_the_old_file_was_deleted_gives_the_same_file()
    {
        WriteLegacy(Late, Early);
        WriteMouseFile(Middle);
        NewSession().For(Mouse, Now);
        var merged = File.ReadAllLines(MouseFile);
        WriteLegacy(Late, Early); // the state after a crash: merged target, old file still there

        NewSession().For(Mouse, Now);

        Assert.Equal(merged, File.ReadAllLines(MouseFile));
        Assert.False(File.Exists(LegacyPath));
    }

    [Fact]
    public void A_leftover_temporary_file_is_replaced_by_the_merge()
    {
        WriteLegacy(Late);
        WriteMouseFile(Early);
        File.WriteAllText(MouseFile + ".tmp", "half written");

        NewSession().For(Mouse, Now);

        Assert.Equal([Early, Late], File.ReadAllLines(MouseFile));
        Assert.False(File.Exists(MouseFile + ".tmp"));
    }

    [Fact]
    public void Merging_keeps_a_line_that_is_not_a_record()
    {
        WriteLegacy(Late, "damaged line");
        WriteMouseFile(Early);

        NewSession().For(Mouse, Now);

        Assert.Contains("damaged line", File.ReadAllLines(MouseFile));
    }

    [WindowsFact]
    public void A_locked_target_keeps_both_files_and_the_move_works_at_the_next_start()
    {
        WriteLegacy(Late);
        WriteMouseFile(Early);

        using (new FileStream(MouseFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            NewSession().For(Mouse, Now);

        Assert.True(File.Exists(LegacyPath));
        NewSession().For(Mouse, Now);
        Assert.Equal([Early, Late], File.ReadAllLines(MouseFile));
        Assert.False(File.Exists(LegacyPath));
    }

    [Fact]
    public void ClearAll_deletes_every_history_file_and_the_old_file_but_keeps_the_folder()
    {
        WriteLegacy(Early);
        WriteMouseFile(Late);
        File.WriteAllText(Path.Combine(HistoryFolder, "FEEDFACE.csv"), Middle); // a device not seen in this session
        var histories = NewSession();
        histories.For(Keyboard, Now).Record(TestData.Status(percent: 50, at: Now));

        Assert.True(histories.ClearAll());

        Assert.Empty(Directory.GetFiles(HistoryFolder));
        Assert.False(File.Exists(LegacyPath));
    }

    [Fact]
    public void ClearAll_succeeds_when_there_is_nothing_to_delete()
    {
        Assert.True(NewSession().ClearAll());
    }
}
