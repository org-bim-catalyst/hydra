using System.Text.Json;
using AskLucy.Domain.Documents;
using AskLucy.Web.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AskLucy.Web.Tests.Middleware;

/// <summary>
/// Covers constitution §5 (a stale RowVersion MUST surface as a caller-visible failure, not
/// bubble as a 500) — specs/002-chat-history-management tasks.md T073.
/// </summary>
public sealed class ProblemDetailsMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_ShouldReturn409_WhenDbUpdateConcurrencyExceptionIsThrown()
    {
        var middleware = new ProblemDetailsMiddleware(
            _ => throw new DbUpdateConcurrencyException("stale row version"),
            NullLogger<ProblemDetailsMiddleware>.Instance,
            NSubstitute.Substitute.For<AskLucy.Application.OperationalFailures.Abstractions.IOperationalFailureRecorder>(),
            new AskLucy.Application.OperationalFailures.FailureClassifier());
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await JsonSerializer.DeserializeAsync<JsonElement>(context.Response.Body, cancellationToken: TestContext.Current.CancellationToken);
        body.GetProperty("status").GetInt32().Should().Be(409);
    }

    // specs/040-composer-interaction-bug-fixes US6 T019 — a bare HttpRequestException (e.g. a
    // network failure during a transcription upload) must map to 502 ai-provider-unavailable,
    // not the generic 500, so the user sees an actionable message and the client can handle it.
    [Fact]
    public async Task InvokeAsync_ShouldReturn502_WhenHttpRequestExceptionIsThrown()
    {
        var middleware = new ProblemDetailsMiddleware(
            _ => throw new HttpRequestException("Connection refused"),
            NullLogger<ProblemDetailsMiddleware>.Instance,
            NSubstitute.Substitute.For<AskLucy.Application.OperationalFailures.Abstractions.IOperationalFailureRecorder>(),
            new AskLucy.Application.OperationalFailures.FailureClassifier());
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status502BadGateway);
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await JsonSerializer.DeserializeAsync<JsonElement>(context.Response.Body, cancellationToken: TestContext.Current.CancellationToken);
        body.GetProperty("status").GetInt32().Should().Be(502);
        body.GetProperty("type").GetString().Should().Contain("ai-provider-unavailable");
    }

    /// <summary>FR-029, contracts/document-processing-api.md: retrying a processing job that isn't currently Failed returns 409 with a machine-readable reason, not just a 400 like a generic domain-rule violation.</summary>
    [Fact]
    public async Task InvokeAsync_ShouldReturn409WithNotInFailedStateReason_WhenProcessingNotInFailedStateExceptionIsThrown()
    {
        var middleware = new ProblemDetailsMiddleware(
            _ => throw new ProcessingNotInFailedStateException(),
            NullLogger<ProblemDetailsMiddleware>.Instance,
            NSubstitute.Substitute.For<AskLucy.Application.OperationalFailures.Abstractions.IOperationalFailureRecorder>(),
            new AskLucy.Application.OperationalFailures.FailureClassifier());
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await JsonSerializer.DeserializeAsync<JsonElement>(context.Response.Body, cancellationToken: TestContext.Current.CancellationToken);
        body.GetProperty("status").GetInt32().Should().Be(409);
        body.GetProperty("reason").GetString().Should().Be("NotInFailedState");
    }

    private static async Task<(int Status, JsonElement Body)> RunAsync(Exception toThrow)
    {
        var middleware = new ProblemDetailsMiddleware(
            _ => throw toThrow,
            NullLogger<ProblemDetailsMiddleware>.Instance,
            NSubstitute.Substitute.For<AskLucy.Application.OperationalFailures.Abstractions.IOperationalFailureRecorder>(),
            new AskLucy.Application.OperationalFailures.FailureClassifier());
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await JsonSerializer.DeserializeAsync<JsonElement>(context.Response.Body, cancellationToken: TestContext.Current.CancellationToken);
        return (context.Response.StatusCode, body);
    }

    /// <summary>specs/079 contracts/site-boundary-edit-api.md: a stale outline revision returns 409 carrying the revision now in force.</summary>
    [Fact]
    public async Task InvokeAsync_ShouldReturn409WithCurrentRevision_WhenTheConflictCarriesOne()
    {
        var (status, body) = await RunAsync(new AskLucy.Domain.Common.ConcurrencyConflictException("The outline changed.", "rev-2"));

        status.Should().Be(StatusCodes.Status409Conflict);
        body.GetProperty("currentRevision").GetString().Should().Be("rev-2");
    }

    [Fact]
    public async Task InvokeAsync_ShouldOmitCurrentRevision_WhenTheConflictHasNone()
    {
        var (_, body) = await RunAsync(new AskLucy.Domain.Common.ConcurrencyConflictException("Changed elsewhere."));

        body.TryGetProperty("currentRevision", out _).Should().BeFalse();
    }

    /// <summary>specs/079: a refused hand-edited shape is 422 and names the ring and the reason.</summary>
    [Fact]
    public async Task InvokeAsync_ShouldReturn422WithRingIndexAndReason_WhenAHandEditedShapeIsRejected()
    {
        var (status, body) = await RunAsync(new AskLucy.Domain.SiteBoundaries.SiteBoundaryGeometryRejectedException(
            1, AskLucy.Domain.SiteBoundaries.SiteBoundaryGeometryRejectedException.SelfCrossing, "Ring 2 crosses itself."));

        status.Should().Be(StatusCodes.Status422UnprocessableEntity);
        body.GetProperty("ringIndex").GetInt32().Should().Be(1);
        body.GetProperty("reason").GetString().Should().Be("selfCrossing");
        body.GetProperty("type").GetString().Should().Contain("site-boundary-rejected");
    }
}
