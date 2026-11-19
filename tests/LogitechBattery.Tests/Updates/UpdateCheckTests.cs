using System.Net;
using LogitechBattery.Updates;

namespace LogitechBattery.Tests;

public class UpdateCheckTests
{
    private const string OlderVersion = "0.7.0";   // the sample release is 0.7.1
    private const string SameVersion = "0.7.1";

    private static Task<UpdateCheckResult> CheckAsync(StubHttpMessageHandler handler, string installedVersion = OlderVersion) =>
        new UpdateCheck(() => handler).CheckAsync(installedVersion, CancellationToken.None);

    private static StubHttpMessageHandler AnswerWith(HttpStatusCode status, string body) => new(status, body);

    private static StubHttpMessageHandler AnswerWithSampleRelease() => AnswerWith(HttpStatusCode.OK, LatestReleaseSample.Json);

    private static string ReasonOf(UpdateCheckResult result) => Assert.IsType<UpdateCheckResult.Failed>(result).Reason;

    [Fact]
    public async Task A_newer_release_is_available_with_its_page()
    {
        var result = await CheckAsync(AnswerWithSampleRelease());

        Assert.Equal(new UpdateCheckResult.Available(new Version(0, 7, 1), LatestReleaseSample.PageUrl), result);
    }

    [Fact]
    public async Task The_same_version_is_up_to_date()
    {
        var result = await CheckAsync(AnswerWithSampleRelease(), SameVersion);

        Assert.Equal(new UpdateCheckResult.UpToDate(new Version(0, 7, 1)), result);
    }

    [Fact]
    public async Task A_dev_build_of_the_same_version_is_up_to_date()
    {
        var result = await CheckAsync(AnswerWithSampleRelease(), "0.7.1-dev.12+abcdef0");

        Assert.IsType<UpdateCheckResult.UpToDate>(result);
    }

    [Fact]
    public async Task A_newer_installed_version_is_up_to_date()
    {
        var result = await CheckAsync(AnswerWithSampleRelease(), "0.8.0");

        Assert.IsType<UpdateCheckResult.UpToDate>(result);
    }

    [Fact]
    public async Task Version_9_is_older_than_version_10()
    {
        var tenth = LatestReleaseSample.JsonWith("tag_name", "v10.0.0");

        var result = await CheckAsync(AnswerWith(HttpStatusCode.OK, tenth), "9.0.0");

        Assert.IsType<UpdateCheckResult.Available>(result);
    }

    [Fact]
    public async Task An_unknown_installed_version_fails_without_a_request()
    {
        var handler = AnswerWithSampleRelease();

        var result = await new UpdateCheck(() => handler).CheckAsync(null, CancellationToken.None);

        Assert.Contains("unknown", ReasonOf(result));
        Assert.Null(handler.RequestUri);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "403")]
    [InlineData(HttpStatusCode.TooManyRequests, "429")]
    [InlineData(HttpStatusCode.NotFound, "404")]
    [InlineData(HttpStatusCode.MovedPermanently, "301")]
    public async Task A_status_other_than_200_fails_and_names_the_code(HttpStatusCode status, string code)
    {
        var result = await CheckAsync(AnswerWith(status, "{}"));

        Assert.Contains(code, ReasonOf(result));
    }

    [Fact]
    public async Task A_network_error_fails()
    {
        var result = await CheckAsync(new StubHttpMessageHandler(new HttpRequestException("No such host is known")));

        Assert.Contains("No such host is known", ReasonOf(result));
    }

    [Fact]
    public async Task A_timeout_fails()
    {
        var result = await CheckAsync(new StubHttpMessageHandler(new TaskCanceledException("The request timed out")));

        Assert.Contains("in time", ReasonOf(result));
    }

    [Fact]
    public async Task A_cancel_by_the_caller_is_not_swallowed()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var check = new UpdateCheck(() => new StubHttpMessageHandler(new TaskCanceledException("cancelled")));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => check.CheckAsync(OlderVersion, cancelled.Token));
    }

    [Fact]
    public async Task A_login_page_of_a_captive_portal_fails()
    {
        var result = await CheckAsync(AnswerWith(HttpStatusCode.OK, "<html><body>Sign in to the Wi-Fi</body></html>"));

        Assert.IsType<UpdateCheckResult.Failed>(result);
    }

    [Fact]
    public async Task An_answer_over_1_MB_fails()
    {
        var tooLarge = new string(' ', UpdateCheck.MaxBodyBytes + 1) + LatestReleaseSample.Json;

        var result = await CheckAsync(AnswerWith(HttpStatusCode.OK, tooLarge));

        Assert.Contains("1 MB", ReasonOf(result));
    }

    [Fact]
    public async Task An_answer_of_exactly_1_MB_is_read()
    {
        var padding = new string(' ', UpdateCheck.MaxBodyBytes - LatestReleaseSample.Json.Length);

        var result = await CheckAsync(AnswerWith(HttpStatusCode.OK, LatestReleaseSample.Json + padding));

        Assert.IsType<UpdateCheckResult.Available>(result);
    }

    [Fact]
    public async Task The_request_goes_to_the_latest_release_url_with_the_user_agent_and_accept_headers()
    {
        var handler = AnswerWithSampleRelease();

        await CheckAsync(handler);

        Assert.Equal("https://api.github.com/repos/fedemarco/logitech-battery-indicator/releases/latest", handler.RequestUri!.AbsoluteUri);
        Assert.Equal("LogitechBattery", handler.RequestHeaders!.UserAgent.ToString());
        Assert.Equal("application/vnd.github+json", handler.RequestHeaders.Accept.ToString());
    }

    [Fact]
    public async Task A_second_check_on_the_same_instance_works()
    {
        var check = new UpdateCheck(AnswerWithSampleRelease);

        await check.CheckAsync(OlderVersion, CancellationToken.None);
        var second = await check.CheckAsync(OlderVersion, CancellationToken.None);

        Assert.IsType<UpdateCheckResult.Available>(second);
    }
}
