using System.Net;
using System.Text;

namespace LogitechBattery.Updates;

/// <summary>
/// Asks GitHub for the latest release. This is the app's only network request. Each check uses a new handler and disposes it,
/// so no socket stays open between two checks.
/// </summary>
public sealed class UpdateCheck
{
    internal const string LatestReleaseUrl = "https://api.github.com/repos/fedemarco/logitech-battery-indicator/releases/latest";
    internal const string UserAgent = "LogitechBattery"; // no version and no id: GitHub gets nothing that tells users apart
    internal const string AcceptedMediaType = "application/vnd.github+json";
    internal const int MaxBodyBytes = 1024 * 1024;

    private const int ReadChunkBytes = 8 * 1024;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    private readonly Func<HttpMessageHandler> _createHandler;

    /// <param name="createHandler">For tests; the default uses the system proxy and follows no redirect, because the answer must come from api.github.com itself.</param>
    public UpdateCheck(Func<HttpMessageHandler>? createHandler = null) =>
        _createHandler = createHandler ?? (() => new HttpClientHandler { AllowAutoRedirect = false });

    /// <summary>Never throws, except OperationCanceledException when <paramref name="cancellationToken"/> is cancelled.</summary>
    public async Task<UpdateCheckResult> CheckAsync(string? currentInformationalVersion, CancellationToken cancellationToken)
    {
        if (AppVersion.ReleaseVersionOf(currentInformationalVersion) is not { } current)
            return new UpdateCheckResult.Failed("the installed version is unknown");

        using var client = new HttpClient(_createHandler());

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);
        try
        {
            return await CheckLatestAsync(client, current, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new UpdateCheckResult.Failed("GitHub did not answer in time");
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            return new UpdateCheckResult.Failed($"network error: {ex.Message}");
        }
    }

    private static async Task<UpdateCheckResult> CheckLatestAsync(HttpClient client, Version current, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUrl);
        request.Headers.UserAgent.ParseAdd(UserAgent);
        request.Headers.Accept.ParseAdd(AcceptedMediaType);

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode != HttpStatusCode.OK)
            return new UpdateCheckResult.Failed($"GitHub answered with status {(int)response.StatusCode}");

        var body = await ReadLimitedAsync(response.Content, cancellationToken);
        return body is null
            ? new UpdateCheckResult.Failed($"the answer is larger than {MaxBodyBytes / (1024 * 1024)} MB")
            : ResultFor(current, body);
    }

    private static async Task<string?> ReadLimitedAsync(HttpContent content, CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var collected = new MemoryStream();
        var chunk = new byte[ReadChunkBytes];
        while (true)
        {
            var count = await stream.ReadAsync(chunk, cancellationToken);
            if (count == 0)
                break;

            collected.Write(chunk, 0, count);
            if (collected.Length > MaxBodyBytes)
                return null;
        }
        return Encoding.UTF8.GetString(collected.GetBuffer(), 0, (int)collected.Length);
    }

    private static UpdateCheckResult ResultFor(Version current, string body)
    {
        if (!LatestRelease.TryParse(body, out var latest, out var problem))
            return new UpdateCheckResult.Failed(problem);

        return latest.Version > current
            ? new UpdateCheckResult.Available(latest.Version, latest.PageUrl)
            : new UpdateCheckResult.UpToDate(current);
    }
}
