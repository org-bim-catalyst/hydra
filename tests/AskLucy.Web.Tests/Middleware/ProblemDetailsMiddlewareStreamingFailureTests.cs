using AskLucy.Web.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AskLucy.Web.Tests.Middleware;

/// <summary>
/// What happens when a request fails <i>after</i> its response is already on the wire — the
/// streaming chat endpoint's normal shape. Both cases here were observed in production logs:
/// a page refresh cancelled an open SSE stream, the middleware tried to turn that into a Problem
/// Details response, and setting the status code threw a second exception from inside the error
/// handler which replaced the real one and reset the connection (the browser reported
/// ERR_HTTP2_PROTOCOL_ERROR against a 200).
/// </summary>
public sealed class ProblemDetailsMiddlewareStreamingFailureTests
{
    /// <summary>A response that has already begun — <see cref="HttpResponse.StatusCode"/>'s setter throws against it, exactly as Kestrel's does.</summary>
    private sealed class StartedResponseFeature : IHttpResponseFeature
    {
        public Stream Body { get; set; } = new MemoryStream();
        public bool HasStarted => true;
        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
        public string? ReasonPhrase { get; set; }
        public int StatusCode { get; set; } = StatusCodes.Status200OK;
        public void OnCompleted(Func<object, Task> callback, object state) { }
        public void OnStarting(Func<object, Task> callback, object state) { }
    }

    private static DefaultHttpContext ContextWithStartedResponse()
    {
        var context = new DefaultHttpContext();
        context.Features.Set<IHttpResponseFeature>(new StartedResponseFeature());
        return context;
    }

    [Fact]
    public async Task InvokeAsync_ShouldNotThrow_WhenTheRequestFailsAfterTheResponseHasStarted()
    {
        var middleware = new ProblemDetailsMiddleware(
            _ => throw new InvalidOperationException("mid-stream failure"),
            NullLogger<ProblemDetailsMiddleware>.Instance,
            NSubstitute.Substitute.For<AskLucy.Application.OperationalFailures.Abstractions.IOperationalFailureRecorder>(),
            new AskLucy.Application.OperationalFailures.FailureClassifier());
        var context = ContextWithStartedResponse();

        var act = () => middleware.InvokeAsync(context);

        // The original failure is logged, the connection ends, and — critically — no second
        // exception escapes the error handler to be reported in its place.
        await act.Should().NotThrowAsync();
        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task InvokeAsync_ShouldSwallowClientDisconnects_WithoutWritingAResponse()
    {
        var middleware = new ProblemDetailsMiddleware(
            _ => throw new OperationCanceledException("client went away"),
            NullLogger<ProblemDetailsMiddleware>.Instance,
            NSubstitute.Substitute.For<AskLucy.Application.OperationalFailures.Abstractions.IOperationalFailureRecorder>(),
            new AskLucy.Application.OperationalFailures.FailureClassifier());
        var context = new DefaultHttpContext { RequestAborted = new CancellationToken(canceled: true) };
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        // Nobody is left to read a response, and nothing failed on the server: the status stays
        // untouched and no Problem Details body is written.
        context.Response.StatusCode.Should().Be(StatusCodes.Status200OK);
        context.Response.Body.Length.Should().Be(0);
    }

    [Fact]
    public async Task InvokeAsync_ShouldStillReportACancellation_ThatTheClientDidNotCause()
    {
        // A cancellation with no client disconnect behind it is a real failure — an internal
        // timeout, say — and must not be quietly swallowed by the disconnect path above.
        var middleware = new ProblemDetailsMiddleware(
            _ => throw new OperationCanceledException("an internal timeout"),
            NullLogger<ProblemDetailsMiddleware>.Instance,
            NSubstitute.Substitute.For<AskLucy.Application.OperationalFailures.Abstractions.IOperationalFailureRecorder>(),
            new AskLucy.Application.OperationalFailures.FailureClassifier());
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        context.Response.Body.Length.Should().BeGreaterThan(0);
    }
}
