using System.Net;
using System.Text;

namespace TCFModManager.Core.Tests;

// Records the last request it saw and returns a canned response, so SpModApiClient can
// be tested without hitting the real network.
internal sealed class FakeHttpMessageHandler(HttpStatusCode statusCode, string responseBody, string? retryAfterSeconds = null)
    : HttpMessageHandler
{
    public Uri? LastRequestUri { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequestUri = request.RequestUri;

        var response = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
        };
        if (retryAfterSeconds is not null)
        {
            response.Headers.Add("Retry-After", retryAfterSeconds);
        }

        return Task.FromResult(response);
    }
}
