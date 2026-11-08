namespace LogitechBattery.Tests;

public class AppLogRedactTests
{
    private const string Profile = @"C:\Users\anna";

    [Fact]
    public void The_profile_folder_becomes_a_placeholder() =>
        Assert.Equal(
            @"Access to %USERPROFILE%\AppData\Local\history.csv is denied",
            AppLog.Redact(@"Access to C:\Users\anna\AppData\Local\history.csv is denied", Profile));

    [Fact]
    public void The_profile_folder_is_found_in_any_letter_case() =>
        Assert.Equal(@"%USERPROFILE%\x", AppLog.Redact(@"c:\users\ANNA\x", Profile));

    [Fact]
    public void Every_occurrence_is_replaced() =>
        Assert.Equal(@"%USERPROFILE%\a and %USERPROFILE%\b", AppLog.Redact(@"C:\Users\anna\a and C:\Users\anna\b", Profile));

    [Fact]
    public void A_longer_folder_name_that_starts_the_same_is_left_alone() =>
        Assert.Equal(@"C:\Users\annabel\x", AppLog.Redact(@"C:\Users\annabel\x", Profile));

    [Fact]
    public void The_bare_user_name_is_left_alone() =>
        Assert.Equal("anna charged the mouse", AppLog.Redact("anna charged the mouse", Profile));

    [Fact]
    public void Text_without_the_profile_folder_is_unchanged() =>
        Assert.Equal("Mouse link up", AppLog.Redact("Mouse link up", Profile));

    [Fact]
    public void Characters_that_mean_something_in_a_pattern_are_taken_literally() =>
        Assert.Equal(@"%USERPROFILE%\x and C:\Users\aXb+c(1)\x", AppLog.Redact(@"C:\Users\a.b+c(1)\x and C:\Users\aXb+c(1)\x", @"C:\Users\a.b+c(1)"));

    [Fact]
    public void A_profile_folder_given_with_a_trailing_backslash_is_still_found() =>
        Assert.Equal(@"%USERPROFILE%\x", AppLog.Redact(@"C:\Users\anna\x", @"C:\Users\anna\"));

    [Fact]
    public void A_bare_drive_is_never_treated_as_a_profile_folder() =>
        Assert.Equal(@"C:\Windows and D:\x", AppLog.Redact(@"C:\Windows and D:\x", @"C:\"));

    [Fact]
    public void An_empty_profile_folder_changes_nothing() =>
        Assert.Equal(@"C:\Users\anna\x", AppLog.Redact(@"C:\Users\anna\x", profileFolder: ""));
}
