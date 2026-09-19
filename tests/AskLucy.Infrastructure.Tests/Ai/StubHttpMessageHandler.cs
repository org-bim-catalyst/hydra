namespace AskLucy.Infrastructure.Tests.Ai;

/// <summary>Shared canned-response handler for provider HTTP tests — avoids a real network call while exercising each provider's request-building/response-parsing code.</summary>
internal sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
{
    public HttpRequestMessage? LastRequest { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        var response = responder(request);

        // A real handler surfaces a cancelled token as OperationCanceledException rather than
        // returning a response. Without this, a responder that outlives its caller's timeout
        // still returns 200 and the call site sees a successful response instead of a cancelled
        // one — which is only observable if some later await happens to check the token. That
        // made the vision-budget test pass locally and fail on a loaded CI runner.
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(response);
    }
}
