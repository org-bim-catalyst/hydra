using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Web.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AskLucy.Web.Tests.Middleware;

/// <summary>
/// specs/032-transcription-and-mode-switch-fixes T004/T009 — proves
/// <see cref="AiProviderRequestInvalidException"/> maps to a 400 Problem Details response with
/// a fixed, safe detail (never the exception's own <c>Message</c>, which may carry the raw
/// upstream provider body — see research.md Decision 1's correction). Kept in its own file
/// rather than added to <c>ProblemDetailsMiddlewareTests.cs</c>, which already carries an
/// unrelated, pre-existing uncommitted change (research.md Decision 5).
/// </summary>
public sealed class AiProviderRequestInvalidExceptionMappingTests
{
    [Fact]
    public async Task InvokeAsync_ShouldReturn400WithSafeDetail_WhenAiProviderRequestInvalidExceptionIsThrown()
    {
        var middleware = new ProblemDetailsMiddleware(
            _ => throw new AiProviderRequestInvalidException("OpenAI rejected the request with 400: {\"error\":\"raw upstream body\"}"),
            NullLogger<ProblemDetailsMiddleware>.Instance);
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await JsonSerializer.DeserializeAsync<JsonElement>(context.Response.Body, cancellationToken: TestContext.Current.CancellationToken);
        body.GetProperty("status").GetInt32().Should().Be(400);
        body.GetProperty("type").GetString().Should().Be("https://hydra.bimcatalyst.com/problems/ai-provider-request-invalid");
        var detail = body.GetProperty("detail").GetString();
        detail.Should().NotBeNullOrWhiteSpace();
        detail.Should().NotContain("raw upstream body");
    }
}
