namespace LogitechBattery.Tests;

public class AppVersionTests
{
    [Fact]
    public void A_release_build_shows_the_version_without_build_metadata() =>
        Assert.Equal("0.5.0", AppVersion.Display("0.5.0+80df081abcdef"));

    [Fact]
    public void A_dev_build_keeps_the_commit_shortened_to_7_characters() =>
        Assert.Equal("0.5.0-dev.42+80df081", AppVersion.Display("0.5.0-dev.42+80df081abcdef"));

    [Fact]
    public void A_dev_build_with_a_short_commit_keeps_it_whole() =>
        Assert.Equal("0.5.0-dev.42+abc", AppVersion.Display("0.5.0-dev.42+abc"));

    [Fact]
    public void A_version_without_build_metadata_is_shown_as_is() =>
        Assert.Equal("0.5.0", AppVersion.Display("0.5.0"));

    [Fact]
    public void A_missing_version_shows_unknown() =>
        Assert.Equal("unknown", AppVersion.Display(null));

    [Theory]
    [InlineData("0.8.0", 0, 8, 0)]
    [InlineData("0.8.0+80df081abcdef", 0, 8, 0)]
    [InlineData("0.8.0-dev.42+80df081", 0, 8, 0)]
    [InlineData("10.20.30", 10, 20, 30)]
    public void The_release_version_is_the_numbers_before_the_prerelease_and_build_parts(string informational, int major, int minor, int patch) =>
        Assert.Equal(new Version(major, minor, patch), AppVersion.ReleaseVersionOf(informational));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unknown")]
    [InlineData("-dev.1")]
    [InlineData("0.8")]
    [InlineData("1.2.3.4")]
    [InlineData("1.2.x")]
    public void A_missing_or_unreadable_version_or_one_without_three_numbers_has_no_release_version(string? informational) =>
        Assert.Null(AppVersion.ReleaseVersionOf(informational));

    [Fact]
    public void Version_10_is_newer_than_version_9() =>
        Assert.True(AppVersion.ReleaseVersionOf("10.0.0") > AppVersion.ReleaseVersionOf("9.0.0"));

    [Fact]
    public void A_dev_build_is_not_newer_than_its_release_version() =>
        Assert.False(AppVersion.ReleaseVersionOf("0.8.0-dev.3+abc") > AppVersion.ReleaseVersionOf("0.8.0"));
}
