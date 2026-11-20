using System.Net;
using System.Net.Http.Headers;

namespace LogitechBattery.Tests;

/// <summary>Answers every request with the given response, or throws the given exception; remembers what the request looked like.</summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpResponseMessage> _respond;

    public StubHttpMessageHandler(HttpStatusCode status, string body) :
        this(() => new HttpResponseMessage(status) { Content = new StringContent(body) })
    {
    }

    public StubHttpMessageHandler(HttpContent content) :
        this(() => new HttpResponseMessage(HttpStatusCode.OK) { Content = content })
    {
    }

    public StubHttpMessageHandler(Exception failure) : this(() => throw failure)
    {
    }

    private StubHttpMessageHandler(Func<HttpResponseMessage> respond) => _respond = respond;

    public Uri? RequestUri { get; private set; }
    public HttpRequestHeaders? RequestHeaders { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RequestUri = request.RequestUri;
        RequestHeaders = request.Headers;
        return Task.FromResult(_respond());
    }
}
