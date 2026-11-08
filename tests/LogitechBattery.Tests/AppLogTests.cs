namespace LogitechBattery.Tests;

public sealed class AppLogTests : IDisposable
{
    private readonly TempFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    private string LogPath => _folder.FileNamed("log.txt");
    private string OldLogPath => _folder.FileNamed("log.old.txt");

    private void Write(string message) => AppLog.Write(message, _folder.Path);

    private void CreateLogOfSize(long bytes) => CreateFileOfSize(LogPath, bytes);

    private static void CreateFileOfSize(string path, long bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[bytes]);
    }

    private void BlockRotationWithAFolderAtTheOldLogPath() => Directory.CreateDirectory(OldLogPath);

    private long LogSize => new FileInfo(LogPath).Length;

    [Fact]
    public void Tests_never_write_to_the_real_data_folder() =>
        Assert.StartsWith(Path.GetTempPath(), AppPaths.DataDirectory);

    [Fact]
    public void Write_appends_a_timestamped_line()
    {
        Write("hello");

        Assert.Matches($@"^\d{{4}}-\d\d-\d\d \d\d:\d\d:\d\d  hello{Environment.NewLine}$", File.ReadAllText(LogPath));
    }

    [Fact]
    public void A_log_at_the_size_limit_is_not_rotated()
    {
        CreateLogOfSize(AppLog.RotateAtBytes);

        Write("one more");

        Assert.False(File.Exists(OldLogPath));
    }

    [Fact]
    public void A_log_over_the_size_limit_moves_to_the_old_log_and_a_new_log_starts()
    {
        CreateLogOfSize(AppLog.RotateAtBytes + 1);

        Write("fresh");

        Assert.Equal(AppLog.RotateAtBytes + 1, new FileInfo(OldLogPath).Length);
        Assert.Single(File.ReadAllLines(LogPath));
    }

    [Fact]
    public void Rotation_replaces_an_existing_old_log()
    {
        Directory.CreateDirectory(_folder.Path);
        File.WriteAllText(OldLogPath, "older");
        CreateLogOfSize(AppLog.RotateAtBytes + 1);

        Write("fresh");

        Assert.Equal(AppLog.RotateAtBytes + 1, new FileInfo(OldLogPath).Length);
    }

    [Fact]
    public void Blocked_rotation_keeps_writing_up_to_the_hard_limit()
    {
        CreateLogOfSize(AppLog.RotateAtBytes + 1);
        BlockRotationWithAFolderAtTheOldLogPath();

        Write("still written");

        Assert.True(LogSize > AppLog.RotateAtBytes + 1);
    }

    [Fact]
    public void Blocked_rotation_stops_writing_over_the_hard_limit()
    {
        CreateLogOfSize(AppLog.StopWritingAtBytes + 1);
        BlockRotationWithAFolderAtTheOldLogPath();

        Write("not written");

        Assert.Equal(AppLog.StopWritingAtBytes + 1, LogSize);
    }

    [Fact]
    public void Write_does_not_throw_when_another_program_locks_the_log()
    {
        Directory.CreateDirectory(_folder.Path);
        using var lockedLog = new FileStream(LogPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);

        Assert.Null(Record.Exception(() => Write("while locked")));
    }

    [Fact]
    public void Write_hides_the_user_profile_folder()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        Write($"Access to {Path.Combine(profile, "history.csv")} is denied");

        var line = File.ReadAllText(LogPath);
        Assert.DoesNotContain(profile, line);
        Assert.Contains("%USERPROFILE%", line);
    }

    [WindowsFact]
    public void Rotation_blocked_by_an_open_reader_keeps_writing_to_the_log()
    {
        CreateLogOfSize(AppLog.RotateAtBytes + 1);
        using var tailTool = new FileStream(LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite); // no delete sharing

        Write("still written");
        tailTool.Dispose();

        Assert.False(File.Exists(OldLogPath));
        Assert.True(LogSize > AppLog.RotateAtBytes + 1);
    }
}
