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
}
