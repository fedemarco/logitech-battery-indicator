using LogitechBattery.Battery;

namespace LogitechBattery.Tests;

public sealed class SettingsFileTests : IDisposable
{
    private static readonly AlertSettings NonDefaultAlerts = new(LowPercent: 20, FullChargeAlert: false);
    private static readonly AppSettings NonDefault = AppSettings.Default with { Alerts = NonDefaultAlerts };
    private static readonly DeviceId MouseId = DeviceId.Parse("A1B2C3D4");
    private static readonly DeviceId KeyboardId = DeviceId.Parse("0A0B0C0D");
    private static readonly DeviceId RouteId = DeviceId.Parse("RC0A1S02");

    private readonly TempFolder _folder = new();
    private string FilePath => _folder.FileNamed("settings.ini");

    public void Dispose() => _folder.Dispose();

    private SettingsFile CreateSettingsFile() => new(FilePath);

    private void WriteFile(params string[] lines)
    {
        Directory.CreateDirectory(_folder.Path);
        File.WriteAllLines(FilePath, lines);
    }

    private static ParsedSettings Parse(params string[] lines) => SettingsFile.Parse(lines);

    [Fact]
    public void Missing_file_gives_the_defaults() =>
        Assert.Equal(AppSettings.Default, CreateSettingsFile().Load());

    [Fact]
    public void Save_creates_a_missing_folder()
    {
        Assert.Null(CreateSettingsFile().Save(NonDefault));

        Assert.True(File.Exists(FilePath));
    }

    [Fact]
    public void Saved_settings_load_back_unchanged()
    {
        CreateSettingsFile().Save(NonDefault);

        Assert.Equal(NonDefaultAlerts, CreateSettingsFile().Load().Alerts);
    }

    [Fact]
    public void Saved_file_has_the_exact_format()
    {
        CreateSettingsFile().Save(NonDefault);

        Assert.Equal(
            [
                "# Logitech Battery settings. Exit the app before you edit this file: it reads the file only at startup.",
                "# lowAlertPercent: one of 10, 15, 20. fullChargeAlert: true or false.",
                "# The trayDevice and device lines are written by the app.",
                "version=2",
                "lowAlertPercent=20",
                "fullChargeAlert=false",
            ],
            File.ReadAllLines(FilePath));
    }

    private static AppSettings WithDevices(DeviceId? trayDevice, params (DeviceId Id, string Name)[] names) =>
        new(NonDefaultAlerts, trayDevice, names.ToDictionary(entry => entry.Id, entry => entry.Name));

    private string[] SaveAndReadLines(AppSettings settings)
    {
        CreateSettingsFile().Save(settings);
        return File.ReadAllLines(FilePath);
    }

    [Fact]
    public void Tray_device_and_device_names_load_back_unchanged()
    {
        CreateSettingsFile().Save(WithDevices(MouseId, (MouseId, "PRO X2"), (RouteId, "Keyboard")));

        var loaded = CreateSettingsFile().Load();

        Assert.Equal(MouseId, loaded.TrayDevice);
        Assert.Equal(new Dictionary<DeviceId, string> { [MouseId] = "PRO X2", [RouteId] = "Keyboard" }, loaded.DeviceNames);
    }

    [Fact]
    public void Saved_version_2_file_has_the_exact_format_with_names_sorted_by_id()
    {
        var lines = SaveAndReadLines(WithDevices(MouseId, (MouseId, "PRO X2"), (RouteId, "Keyboard"), (KeyboardId, "Dock")));

        Assert.Equal(
            [
                "# Logitech Battery settings. Exit the app before you edit this file: it reads the file only at startup.",
                "# lowAlertPercent: one of 10, 15, 20. fullChargeAlert: true or false.",
                "# The trayDevice and device lines are written by the app.",
                "version=2",
                "lowAlertPercent=20",
                "fullChargeAlert=false",
                "trayDevice=A1B2C3D4",
                "device.0A0B0C0D.name=Dock",
                "device.A1B2C3D4.name=PRO X2",
                "device.RC0A1S02.name=Keyboard",
            ],
            lines);
    }

    [Fact]
    public void Version_1_file_loads_without_devices_and_is_saved_as_version_2()
    {
        WriteFile("version=1", "lowAlertPercent=20", "fullChargeAlert=false");
        var settings = CreateSettingsFile();

        var loaded = settings.Load();

        Assert.Equal(NonDefaultAlerts, loaded.Alerts);
        Assert.Null(loaded.TrayDevice);
        Assert.Empty(loaded.DeviceNames);
        Assert.Null(settings.Save(loaded));
        Assert.Contains("version=2", File.ReadAllLines(FilePath));
    }

    [Fact]
    public void Invalid_tray_device_is_ignored_with_one_problem()
    {
        var parsed = Parse("trayDevice=not-an-id");

        Assert.Null(parsed.Settings.TrayDevice);
        Assert.Contains("trayDevice", Assert.Single(parsed.Problems));
    }

    [Fact]
    public void Device_name_key_with_an_invalid_id_is_an_unknown_key()
    {
        var parsed = Parse("device.a1b2c3d4.name=Mouse");

        Assert.Empty(parsed.Settings.DeviceNames);
        Assert.Contains("device.a1b2c3d4.name", Assert.Single(parsed.Problems));
    }

    [Fact]
    public void Device_name_key_with_an_invalid_id_is_dropped_at_the_next_save()
    {
        WriteFile("device.zzz.name=Mouse");
        var settings = CreateSettingsFile();

        settings.Save(settings.Load());

        Assert.DoesNotContain("device.zzz", File.ReadAllText(FilePath));
    }

    [Fact]
    public void Device_name_with_a_line_break_or_control_characters_is_cleaned_when_written()
    {
        var lines = SaveAndReadLines(WithDevices(null, (MouseId, "PRO\r\nX2\u0007\tfoo=bar")));

        Assert.Equal("device.A1B2C3D4.name=PROX2foo=bar", lines[^1]);
        Assert.Equal(7, lines.Length);
    }

    [Fact]
    public void Device_name_longer_than_64_characters_is_cut_when_written()
    {
        var lines = SaveAndReadLines(WithDevices(null, (MouseId, new string('x', 100))));

        Assert.Equal("device.A1B2C3D4.name=" + new string('x', 64), lines[^1]);
    }

    [Fact]
    public void Device_name_that_is_empty_after_cleaning_is_not_written()
    {
        var lines = SaveAndReadLines(WithDevices(null, (MouseId, "\r\n\t "), (KeyboardId, "")));

        Assert.DoesNotContain(lines, line => line.StartsWith("device."));
    }

    [Fact]
    public void Version_3_file_keeps_its_device_values_readable_and_is_never_overwritten()
    {
        WriteFile("version=3", "trayDevice=A1B2C3D4", "device.A1B2C3D4.name=PRO X2");
        var before = File.ReadAllBytes(FilePath);
        var settings = CreateSettingsFile();

        var loaded = settings.Load();

        Assert.Equal(MouseId, loaded.TrayDevice);
        Assert.Equal("PRO X2", loaded.DeviceNames[MouseId]);
        Assert.NotNull(settings.Save(loaded));
        Assert.Equal(before, File.ReadAllBytes(FilePath));
    }

    [Fact]
    public void Save_leaves_no_temp_file()
    {
        CreateSettingsFile().Save(NonDefault);

        Assert.Equal([FilePath], Directory.GetFiles(_folder.Path));
    }

    [Fact]
    public void Empty_file_gives_the_defaults_and_can_be_saved()
    {
        WriteFile();
        var settings = CreateSettingsFile();

        Assert.Equal(AlertSettings.Default, settings.Load().Alerts);
        Assert.Null(settings.Save(NonDefault));
    }

    [Fact]
    public void Comments_blank_lines_and_spaces_around_keys_and_values_are_ignored()
    {
        var parsed = Parse("# a comment", "", "  lowAlertPercent  =  20  ", "\tfullChargeAlert=FALSE");

        Assert.Equal(NonDefaultAlerts, parsed.Settings.Alerts);
        Assert.Empty(parsed.Problems);
    }

    [Fact]
    public void Missing_version_counts_as_the_supported_version() =>
        Assert.False(Parse("lowAlertPercent=20").IsNewerVersion);

    [Theory]
    [InlineData("3")]
    [InlineData("0")]
    [InlineData("abc")]
    [InlineData("")]
    public void Any_other_version_counts_as_newer_but_its_known_values_are_read(string version)
    {
        var parsed = Parse($"version={version}", "lowAlertPercent=20");

        Assert.True(parsed.IsNewerVersion);
        Assert.Equal(20, parsed.Settings.Alerts.LowPercent);
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

        Assert.Equal(new AlertSettings(AlertSettings.Default.LowPercent, FullChargeAlert: false), parsed.Settings.Alerts);
        Assert.Single(parsed.Problems);
    }

    [Fact]
    public void A_bad_full_charge_value_falls_back_to_its_default_and_keeps_the_other_value()
    {
        var parsed = Parse("lowAlertPercent=20", "fullChargeAlert=yes");

        Assert.Equal(new AlertSettings(LowPercent: 20, AlertSettings.Default.FullChargeAlert), parsed.Settings.Alerts);
        Assert.Single(parsed.Problems);
    }

    [Fact]
    public void Unknown_key_is_reported() =>
        Assert.Contains("lowAlertPercnt", Assert.Single(Parse("lowAlertPercnt=20").Problems));

    [Fact]
    public void Unknown_key_is_dropped_at_the_next_save()
    {
        WriteFile("lowAlertPercnt=20");
        var settings = CreateSettingsFile();
        settings.Load();

        settings.Save(NonDefault);

        Assert.DoesNotContain("lowAlertPercnt", File.ReadAllText(FilePath));
    }

    [Fact]
    public void Key_in_another_letter_case_is_unknown()
    {
        var parsed = Parse("LowAlertPercent=20");

        Assert.Equal(AlertSettings.Default, parsed.Settings.Alerts);
        Assert.Single(parsed.Problems);
    }

    [Fact]
    public void Line_without_an_equals_sign_is_reported_and_ignored()
    {
        var parsed = Parse("hello", "lowAlertPercent=20");

        Assert.Equal(20, parsed.Settings.Alerts.LowPercent);
        Assert.Contains("line 1", Assert.Single(parsed.Problems));
    }

    [Fact]
    public void The_last_value_wins_for_a_repeated_key()
    {
        var parsed = Parse("lowAlertPercent=10", "lowAlertPercent=20");

        Assert.Equal(20, parsed.Settings.Alerts.LowPercent);
        Assert.Empty(parsed.Problems);
    }

    [Fact]
    public void File_with_a_byte_order_mark_loads()
    {
        Directory.CreateDirectory(_folder.Path);
        File.WriteAllText(FilePath, "lowAlertPercent=20" + Environment.NewLine, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        Assert.Equal(20, CreateSettingsFile().Load().Alerts.LowPercent);
    }

    [Fact]
    public void Newer_version_is_never_overwritten()
    {
        WriteFile("version=3", "lowAlertPercent=20", "somethingNew=1");
        var before = File.ReadAllBytes(FilePath);
        var settings = CreateSettingsFile();

        Assert.Equal(20, settings.Load().Alerts.LowPercent);
        Assert.NotNull(settings.Save(AppSettings.Default));
        Assert.Equal(before, File.ReadAllBytes(FilePath));
    }

    [Fact]
    public void File_over_64_KB_gives_the_defaults_and_is_never_overwritten()
    {
        Directory.CreateDirectory(_folder.Path);
        File.WriteAllText(FilePath, new string('#', (int)SettingsFile.MaxBytes + 1));
        var before = File.ReadAllBytes(FilePath);
        var settings = CreateSettingsFile();

        Assert.Equal(AppSettings.Default, settings.Load());
        Assert.NotNull(settings.Save(NonDefault));
        Assert.Equal(before, File.ReadAllBytes(FilePath));
    }

    [Fact]
    public void File_locked_at_startup_gives_the_defaults_and_is_never_overwritten()
    {
        WriteFile("lowAlertPercent=20");
        var before = File.ReadAllBytes(FilePath);
        var settings = CreateSettingsFile();

        using (new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.Equal(AppSettings.Default, settings.Load());

        Assert.NotNull(settings.Save(NonDefault));
        Assert.Equal(before, File.ReadAllBytes(FilePath));
    }

    [Fact]
    public void Temp_file_left_by_a_crash_is_ignored_and_replaced()
    {
        WriteFile("lowAlertPercent=20");
        File.WriteAllText(FilePath + ".tmp", "garbage");
        var settings = CreateSettingsFile();

        Assert.Equal(20, settings.Load().Alerts.LowPercent);
        settings.Save(AppSettings.Default);

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
        var settings = CreateSettingsFile();
        settings.Load();

        using (new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.NotNull(settings.Save(AppSettings.Default));

        Assert.Equal(before, File.ReadAllBytes(FilePath));
        Assert.Equal([FilePath], Directory.GetFiles(_folder.Path));
    }
}
