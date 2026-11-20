using System.Text.Json.Nodes;
using LogitechBattery.Updates;

namespace LogitechBattery.Tests;

public class LatestReleaseTests
{
    private static LatestRelease? Parse(string json) =>
        LatestRelease.TryParse(json, out var release, out var problemNotNeeded) ? release : null;

    private static string ProblemOf(string json)
    {
        Assert.False(LatestRelease.TryParse(json, out var release, out var problem));
        Assert.Null(release);
        return problem;
    }

    [Fact]
    public void A_real_answer_gives_its_version_and_page()
    {
        var release = Parse(LatestReleaseSample.Json);

        Assert.Equal(new LatestRelease(new Version(0, 7, 1), LatestReleaseSample.PageUrl), release);
    }

    [Theory]
    [InlineData("v1.0")]
    [InlineData("v1.0.0-rc1")]
    [InlineData("1.0.0")]
    [InlineData("v1.0.0.0")]
    [InlineData("v1234567890.0.0")]
    [InlineData("v\u0663.0.0")] // Arabic-Indic digit three
    [InlineData("v1.0.0\n")]
    [InlineData("")]
    public void A_tag_that_is_not_vX_Y_Z_is_refused(string tag) =>
        Assert.Contains("tag", ProblemOf(LatestReleaseSample.JsonWith("tag_name", tag)));

    [Fact]
    public void A_tag_with_9_digits_in_a_part_is_accepted()
    {
        var release = Parse(LatestReleaseSample.JsonWith("tag_name", "v999999999.0.1"));

        Assert.Equal(new Version(999999999, 0, 1), release!.Version);
    }

    [Fact]
    public void The_page_is_built_from_the_tag()
    {
        var release = Parse(LatestReleaseSample.JsonWith("tag_name", "v1.2.3"));

        Assert.Equal("https://github.com/fedemarco/logitech-battery-indicator/releases/tag/v1.2.3", release!.PageUrl);
    }

    [Theory]
    [InlineData("https://github.com/fedemarco/logitech-battery-indicator/releases/../../evil")]
    [InlineData("https://evil.example/\"quoted\"")]
    [InlineData("https://evil.example/with space")]
    [InlineData("https://evil.example/line\nbreak")]
    [InlineData("")]
    public void The_html_url_of_the_answer_has_no_effect_on_the_page(string hostileHtmlUrl)
    {
        var release = Parse(LatestReleaseSample.JsonWith("html_url", hostileHtmlUrl));

        Assert.Equal(LatestReleaseSample.PageUrl, release!.PageUrl);
    }

    [Fact]
    public void An_answer_without_html_url_is_accepted() =>
        Assert.NotNull(Parse(LatestReleaseSample.JsonWithout("html_url")));

    [Fact]
    public void A_draft_is_refused() =>
        Assert.Contains("draft", ProblemOf(LatestReleaseSample.JsonWith("draft", true)));

    [Fact]
    public void A_prerelease_is_refused() =>
        Assert.Contains("prerelease", ProblemOf(LatestReleaseSample.JsonWith("prerelease", true)));

    [Theory]
    [InlineData("tag_name")]
    [InlineData("draft")]
    [InlineData("prerelease")]
    public void A_missing_field_is_refused(string field) =>
        Assert.NotEmpty(ProblemOf(LatestReleaseSample.JsonWithout(field)));

    public static TheoryData<string, JsonNode?> WrongTypedFields => new()
    {
        { "tag_name", 7 },
        { "tag_name", null },
        { "tag_name", new JsonArray() },
        { "draft", "false" },
        { "draft", null },
        { "prerelease", 0 },
    };

    [Theory]
    [MemberData(nameof(WrongTypedFields))]
    public void A_field_of_the_wrong_type_is_refused(string field, JsonNode? wrongValue) =>
        Assert.NotEmpty(ProblemOf(LatestReleaseSample.JsonWith(field, wrongValue)));

    [Fact]
    public void Truncated_JSON_is_refused() =>
        Assert.NotEmpty(ProblemOf(LatestReleaseSample.Json[..100]));

    [Fact]
    public void A_JSON_array_is_refused() =>
        Assert.NotEmpty(ProblemOf($"[{LatestReleaseSample.Json}]"));

    [Fact]
    public void An_empty_string_is_refused() =>
        Assert.NotEmpty(ProblemOf(""));

    [Fact]
    public void An_HTML_page_is_refused() =>
        Assert.NotEmpty(ProblemOf("<html><body>Sign in to the Wi-Fi</body></html>"));

    [Fact]
    public void Text_with_an_unpaired_surrogate_is_refused_without_throwing() =>
        Assert.NotEmpty(ProblemOf("{\"tag_name\":\"v1.0.0\ud800\"}"));
}
