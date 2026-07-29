using System.Text.Json;
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
            NullLogger<ProblemDetailsMiddleware>.Instance);
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await JsonSerializer.DeserializeAsync<JsonElement>(context.Response.Body);
        body.GetProperty("status").GetInt32().Should().Be(409);
    }
}
