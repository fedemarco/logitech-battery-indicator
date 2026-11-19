namespace LogitechBattery.Tests;

public class LogLineTests
{
    private static readonly DateTimeOffset Time = new(2026, 11, 19, 12, 7, 49, 123, TimeSpan.FromHours(-3));

    private static string Format(LogLevel level, string message, params (string Key, object? Value)[] fields) =>
        LogLine.Format(Time, level, message, fields);

    private static string FormatValue(object? value) =>
        Format(LogLevel.Info, "m", ("key", value)).Substring("2026-11-19T12:07:49.123-03:00 INFO  m key=".Length);

    [Fact]
    public void The_time_has_milliseconds_and_the_utc_offset() =>
        Assert.StartsWith("2026-11-19T12:07:49.123-03:00 ", Format(LogLevel.Info, "m"));

    [Fact]
    public void The_time_uses_a_plus_sign_for_an_offset_east_of_utc() =>
        Assert.StartsWith("2026-11-19T12:07:49.123+05:30 ",
            LogLine.Format(new DateTimeOffset(2026, 11, 19, 12, 7, 49, 123, new TimeSpan(5, 30, 0)), LogLevel.Info, "m", []));

    [Theory]
    [InlineData(LogLevel.Info, "INFO  m")]
    [InlineData(LogLevel.Warn, "WARN  m")]
    [InlineData(LogLevel.Error, "ERROR m")]
    public void The_level_is_padded_to_five_characters(LogLevel level, string expectedEnd) =>
        Assert.EndsWith(" " + expectedEnd, Format(level, "m"));

    [Fact]
    public void A_line_without_fields_ends_with_the_message() =>
        Assert.Equal("2026-11-19T12:07:49.123-03:00 INFO  PC resumed", Format(LogLevel.Info, "PC resumed"));

    [Fact]
    public void Fields_follow_the_message_in_order() =>
        Assert.Equal(
            "2026-11-19T12:07:49.123-03:00 INFO  Device found pid=C54D devIdx=01",
            Format(LogLevel.Info, "Device found", ("pid", "C54D"), ("devIdx", "01")));

    [Fact]
    public void A_value_without_special_characters_is_written_bare() => Assert.Equal("C54D", FormatValue("C54D"));

    [Fact]
    public void A_number_is_written_bare() => Assert.Equal("85", FormatValue(85));

    [Fact]
    public void A_decimal_number_uses_the_invariant_culture() => Assert.Equal("1.5", FormatValue(1.5));

    [Fact]
    public void A_value_with_a_space_is_quoted() => Assert.Equal("\"PRO X 2\"", FormatValue("PRO X 2"));

    [Fact]
    public void A_value_with_an_equals_sign_is_quoted() => Assert.Equal("\"a=b\"", FormatValue("a=b"));

    [Fact]
    public void A_double_quote_inside_a_value_is_escaped() => Assert.Equal("\"say \\\"hi\\\"\"", FormatValue("say \"hi\""));

    [Fact]
    public void A_backslash_inside_a_value_is_escaped() => Assert.Equal("\"C:\\\\x\"", FormatValue(@"C:\x"));

    [Fact]
    public void A_newline_inside_a_value_is_escaped() => Assert.Equal("\"a\\nb\"", FormatValue("a\nb"));

    [Fact]
    public void A_carriage_return_inside_a_value_is_escaped() => Assert.Equal("\"a\\rb\"", FormatValue("a\rb"));

    [Fact]
    public void A_tab_inside_a_value_is_escaped() => Assert.Equal("\"a\\tb\"", FormatValue("a\tb"));

    [Fact]
    public void Another_control_character_is_escaped_as_a_unicode_code() => Assert.Equal("\"a\\u001Bb\"", FormatValue("a\u001Bb"));

    [Fact]
    public void An_empty_value_is_written_as_empty_quotes() => Assert.Equal("\"\"", FormatValue(""));

    [Fact]
    public void A_null_value_is_written_as_empty_quotes() => Assert.Equal("\"\"", FormatValue(null));

    [Fact]
    public void An_exception_with_a_stack_trace_becomes_one_line()
    {
        var multiLineText = "System.InvalidOperationException: broken\r\n   at Program.Main()\r\n   at Program.Run()";

        var line = Format(LogLevel.Error, "Startup failed", ("stack", multiLineText));

        Assert.DoesNotContain('\n', line);
        Assert.DoesNotContain('\r', line);
        Assert.EndsWith("stack=\"System.InvalidOperationException: broken\\r\\n   at Program.Main()\\r\\n   at Program.Run()\"", line);
    }
}
