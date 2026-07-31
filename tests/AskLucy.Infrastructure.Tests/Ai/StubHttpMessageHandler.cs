namespace AskLucy.Infrastructure.Tests.Ai;

/// <summary>Shared canned-response handler for provider HTTP tests — avoids a real network call while exercising each provider's request-building/response-parsing code.</summary>
internal sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
{
    public HttpRequestMessage? LastRequest { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        return Task.FromResult(responder(request));
    }
}
