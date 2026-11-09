using LogitechBattery.Battery;

namespace LogitechBattery.Tests;

public sealed class SettingsFileTests : IDisposable
{
    private static readonly AlertSettings NonDefault = new(LowPercent: 20, FullChargeAlert: false);

    private readonly TempFolder _folder = new();
    private string FilePath => _folder.FileNamed("settings.ini");

    public void Dispose() => _folder.Dispose();

    private SettingsFile Settings() => new(FilePath);

    private void WriteFile(params string[] lines)
    {
        Directory.CreateDirectory(_folder.Path);
        File.WriteAllLines(FilePath, lines);
    }

    private static ParsedSettings Parse(params string[] lines) => SettingsFile.Parse(lines);

    [Fact]
    public void Missing_file_gives_the_defaults() =>
        Assert.Equal(AlertSettings.Default, Settings().Load());

    [Fact]
    public void Save_creates_a_missing_folder()
    {
        Assert.Null(Settings().Save(NonDefault));

        Assert.True(File.Exists(FilePath));
    }

    [Fact]
    public void Saved_settings_load_back_unchanged()
    {
        Settings().Save(NonDefault);

        Assert.Equal(NonDefault, Settings().Load());
    }

    [Fact]
    public void Saved_file_has_the_exact_format()
    {
        Settings().Save(NonDefault);

        Assert.Equal(
            [
                "# Logitech Battery settings. Exit the app before you edit this file: it reads the file only at startup.",
                "# lowAlertPercent: one of 10, 15, 20. fullChargeAlert: true or false.",
                "version=1",
                "lowAlertPercent=20",
                "fullChargeAlert=false",
            ],
            File.ReadAllLines(FilePath));
    }

    [Fact]
    public void Save_leaves_no_temp_file()
    {
        Settings().Save(NonDefault);

        Assert.Equal([FilePath], Directory.GetFiles(_folder.Path));
    }

    [Fact]
    public void Empty_file_gives_the_defaults_and_can_be_saved()
    {
        WriteFile();
        var settings = Settings();

        Assert.Equal(AlertSettings.Default, settings.Load());
        Assert.Null(settings.Save(NonDefault));
    }

    [Fact]
    public void Comments_blank_lines_and_spaces_around_keys_and_values_are_ignored()
    {
        var parsed = Parse("# a comment", "", "  lowAlertPercent  =  20  ", "\tfullChargeAlert=FALSE");

        Assert.Equal(NonDefault, parsed.Settings);
        Assert.Empty(parsed.Problems);
    }

    [Fact]
    public void Missing_version_counts_as_the_supported_version() =>
        Assert.False(Parse("lowAlertPercent=20").IsNewerVersion);

    [Theory]
    [InlineData("2")]
    [InlineData("0")]
    [InlineData("abc")]
    [InlineData("")]
    public void Any_other_version_counts_as_newer_but_its_known_values_are_read(string version)
    {
        var parsed = Parse($"version={version}", "lowAlertPercent=20");

        Assert.True(parsed.IsNewerVersion);
        Assert.Equal(20, parsed.Settings.LowPercent);
    }

    [Theory]
    [InlineData("12")]
    [InlineData("25")]
    [InlineData("-5")]
    [InlineData("abc")]
    [InlineData("15 %")]
    [InlineData("+15")]
    [InlineData("")]
    public void A_bad_low_level_falls_back_to_its_default_and_keeps_the_other_value(string badValue)
    {
        var parsed = Parse($"lowAlertPercent={badValue}", "fullChargeAlert=false");

        Assert.Equal(new AlertSettings(AlertSettings.Default.LowPercent, FullChargeAlert: false), parsed.Settings);
        Assert.Single(parsed.Problems);
    }

    [Fact]
    public void A_bad_full_charge_value_falls_back_to_its_default_and_keeps_the_other_value()
    {
        var parsed = Parse("lowAlertPercent=20", "fullChargeAlert=yes");

        Assert.Equal(new AlertSettings(LowPercent: 20, AlertSettings.Default.FullChargeAlert), parsed.Settings);
        Assert.Single(parsed.Problems);
    }

    [Fact]
    public void Unknown_key_is_reported() =>
        Assert.Contains("lowAlertPercnt", Assert.Single(Parse("lowAlertPercnt=20").Problems));

    [Fact]
    public void Unknown_key_is_dropped_at_the_next_save()
    {
        WriteFile("lowAlertPercnt=20");
        var settings = Settings();
        settings.Load();

        settings.Save(NonDefault);

        Assert.DoesNotContain("lowAlertPercnt", File.ReadAllText(FilePath));
    }

    [Fact]
    public void Key_in_another_letter_case_is_unknown()
    {
        var parsed = Parse("LowAlertPercent=20");

        Assert.Equal(AlertSettings.Default, parsed.Settings);
        Assert.Single(parsed.Problems);
    }

    [Fact]
    public void Line_without_an_equals_sign_is_reported_and_ignored()
    {
        var parsed = Parse("hello", "lowAlertPercent=20");

        Assert.Equal(20, parsed.Settings.LowPercent);
        Assert.Contains("line 1", Assert.Single(parsed.Problems));
    }

    [Fact]
    public void The_last_value_wins_for_a_repeated_key()
    {
        var parsed = Parse("lowAlertPercent=10", "lowAlertPercent=20");

        Assert.Equal(20, parsed.Settings.LowPercent);
        Assert.Empty(parsed.Problems);
    }

    [Fact]
    public void File_with_a_byte_order_mark_loads()
    {
        Directory.CreateDirectory(_folder.Path);
        File.WriteAllText(FilePath, "lowAlertPercent=20" + Environment.NewLine, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        Assert.Equal(20, Settings().Load().LowPercent);
    }

    [Fact]
    public void Newer_version_is_never_overwritten()
    {
        WriteFile("version=2", "lowAlertPercent=20", "somethingNew=1");
        var before = File.ReadAllBytes(FilePath);
        var settings = Settings();

        Assert.Equal(20, settings.Load().LowPercent);
        Assert.NotNull(settings.Save(AlertSettings.Default));
        Assert.Equal(before, File.ReadAllBytes(FilePath));
    }

    [Fact]
    public void File_over_64_KB_gives_the_defaults_and_is_never_overwritten()
    {
        Directory.CreateDirectory(_folder.Path);
        File.WriteAllText(FilePath, new string('#', (int)SettingsFile.MaxBytes + 1));
        var before = File.ReadAllBytes(FilePath);
        var settings = Settings();

        Assert.Equal(AlertSettings.Default, settings.Load());
        Assert.NotNull(settings.Save(NonDefault));
        Assert.Equal(before, File.ReadAllBytes(FilePath));
    }

    [Fact]
    public void File_locked_at_startup_gives_the_defaults_and_is_never_overwritten()
    {
        WriteFile("lowAlertPercent=20");
        var before = File.ReadAllBytes(FilePath);
        var settings = Settings();

        using (new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.Equal(AlertSettings.Default, settings.Load());

        Assert.NotNull(settings.Save(NonDefault));
        Assert.Equal(before, File.ReadAllBytes(FilePath));
    }

    [Fact]
    public void Temp_file_left_by_a_crash_is_ignored_and_replaced()
    {
        WriteFile("lowAlertPercent=20");
        File.WriteAllText(FilePath + ".tmp", "garbage");
        var settings = Settings();

        Assert.Equal(20, settings.Load().LowPercent);
        settings.Save(AlertSettings.Default);

        Assert.Equal([FilePath], Directory.GetFiles(_folder.Path));
    }

    [Fact]
    public void Save_returns_a_reason_when_the_folder_cannot_be_created()
    {
        Directory.CreateDirectory(_folder.Path);
        var blocker = _folder.FileNamed("blocker");
        File.WriteAllText(blocker, "a file, not a folder");

        Assert.NotNull(new SettingsFile(Path.Combine(blocker, "settings.ini")).Save(NonDefault));
    }

    [WindowsFact]
    public void Save_while_another_program_has_the_file_open_keeps_the_old_file()
    {
        WriteFile("lowAlertPercent=20");
        var before = File.ReadAllBytes(FilePath);
        var settings = Settings();
        settings.Load();

        using (new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.NotNull(settings.Save(AlertSettings.Default));

        Assert.Equal(before, File.ReadAllBytes(FilePath));
        Assert.Equal([FilePath], Directory.GetFiles(_folder.Path));
    }
}
