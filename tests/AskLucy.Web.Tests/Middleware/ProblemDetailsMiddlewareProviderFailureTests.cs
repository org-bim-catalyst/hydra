using System.Security.Claims;
using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Domain.Ai;
using AskLucy.Web.Middleware;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AskLucy.Web.Tests.Middleware;

/// <summary>
/// specs/043 US1 — every provider-originated failure reaches the caller as its own classified
/// Problem Details response, and the classification itself is disclosed to administrators only
/// (FR-015a).
/// </summary>
public sealed class ProblemDetailsMiddlewareProviderFailureTests
{
    public static TheoryData<AiProviderException, int, string> ClassifiedFailures() => new()
    {
        { new AiProviderAuthenticationException("Rejected."), 502, "ai-provider-authentication-failed" },
        { new AiProviderCredentialUnreadableException("Unreadable."), 502, "ai-provider-credential-unreadable" },
        // 503, not 502: nothing was asked of the provider, so there is nothing to try again
        // (specs/068). Asserted here rather than only in the dedicated test below so a future
        // edit to the mapping cannot quietly put it back on the "please try again" branch.
        { new AiProviderNotConfiguredException("Not configured."), 503, "ai-provider-not-configured" },
        { new AiProviderQuotaExhaustedException("Quota exhausted."), 429, "ai-provider-quota-exhausted" },
        { new AiProviderRateLimitedException("Rate limited."), 429, "ai-provider-rate-limited" },
        { new AiProviderUsageRestrictedException("Restricted."), 502, "ai-provider-usage-restricted" },
        { new AiProviderUnavailableException("Unavailable."), 502, "ai-provider-unavailable" },
        { new AiProviderRequestInvalidException("Invalid."), 400, "ai-provider-request-invalid" },
        { new AiProviderResponseInvalidException("Unparseable."), 502, "ai-provider-response-invalid" },
    };

    [Theory]
    [MemberData(nameof(ClassifiedFailures))]
    public async Task InvokeAsync_ShouldMapEveryClassification_ToItsOwnStatusAndType(
        AiProviderException exception, int expectedStatus, string expectedTypeSuffix)
    {
        var body = await InvokeAsync(exception, asAdministrator: true);

        body.Status.Should().Be(expectedStatus);
        body.Element.GetProperty("type").GetString().Should().EndWith(expectedTypeSuffix);
    }

    [Theory]
    [MemberData(nameof(ClassifiedFailures))]
    public async Task InvokeAsync_ShouldNeverProduceTheGenericFallback_ForAProviderFailure(
        AiProviderException exception, int expectedStatus, string expectedTypeSuffix)
    {
        _ = expectedStatus;
        _ = expectedTypeSuffix;

        // SC-002 — the exact string the reported bug showed for every Gemini sync failure.
        var body = await InvokeAsync(exception, asAdministrator: true);

        body.Element.GetProperty("detail").GetString().Should().NotContain("An unexpected error occurred");
        body.Status.Should().NotBe(500);
    }

    [Theory]
    [MemberData(nameof(ClassifiedFailures))]
    public async Task InvokeAsync_ShouldNeverLeakSensitiveMaterial_ForAnyClassification(
        AiProviderException exception, int expectedStatus, string expectedTypeSuffix)
    {
        _ = expectedStatus;
        _ = expectedTypeSuffix;

        var body = await InvokeAsync(exception, asAdministrator: true);
        var raw = body.Element.GetRawText();

        // SC-008.
        raw.Should().NotContain("sk-");
        raw.Should().NotContain("Exception");
        raw.Should().NotContain("   at ");
    }

    [Fact]
    public async Task InvokeAsync_ShouldDiscloseTheClassification_ToAnAdministrator()
    {
        var body = await InvokeAsync(new AiProviderQuotaExhaustedException("Quota exhausted.", TimeSpan.FromSeconds(60)), asAdministrator: true);

        body.Element.GetProperty("detail").GetString().Should().Be("Quota exhausted.");
        var failure = body.Element.GetProperty("providerFailure");
        failure.GetProperty("kind").GetString().Should().Be(nameof(AiProviderFailureKind.QuotaExhausted));
        failure.GetProperty("canAdministratorAct").GetBoolean().Should().BeFalse();
        failure.GetProperty("retryAfterSeconds").GetInt32().Should().Be(60);
    }

    [Fact]
    public async Task InvokeAsync_ShouldWithholdTheClassification_FromANonAdministrator()
    {
        // FR-015a. An exhausted commercial allowance is tenant operational state; without this
        // gate any end user could read it out of a chat response in devtools.
        var body = await InvokeAsync(new AiProviderQuotaExhaustedException("Quota exhausted."), asAdministrator: false);

        body.Element.TryGetProperty("providerFailure", out _).Should().BeFalse();
        body.Element.GetProperty("detail").GetString().Should().NotContain("quota");
        body.Element.GetProperty("detail").GetString().Should().Be(AskLucy.Application.OperationalFailures.UserFacingFailureText.Later);
    }

    [Fact]
    public async Task InvokeAsync_ShouldNotTellAUser_ToRetryAnUnconfiguredProvider()
    {
        // specs/068. Reported against dictation: with ElevenLabs switched off, every press of the
        // microphone produced a 502 saying "Please try again", for a condition no amount of trying
        // will change. The status and the wording are both part of the fix — a client that treats
        // 502 as transient will keep retrying however the detail is phrased.
        var body = await InvokeAsync(
            new AiProviderNotConfiguredException("ElevenLabs is switched off under Admin → AI providers."),
            asAdministrator: false);

        // specs/074 research D9: the detail is the "later" sentence, never an immediate retry and
        // never "an administrator" (SC-001); the 503 is what stops a client retrying.
        body.Status.Should().Be(503);
        body.Element.GetProperty("detail").GetString().Should().Be(AskLucy.Application.OperationalFailures.UserFacingFailureText.Later);

        // FR-015a still applies: which provider, and that it was switched off rather than never
        // set up, stays administrator-only.
        body.Element.GetProperty("detail").GetString().Should().NotContain("ElevenLabs");
        body.Element.TryGetProperty("providerFailure", out _).Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_ShouldMarkAnActionableFailure_AsAdministratorActionable()
    {
        var body = await InvokeAsync(new AiProviderUsageRestrictedException("Billing disabled."), asAdministrator: true);

        body.Element.GetProperty("providerFailure").GetProperty("canAdministratorAct").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_ShouldEmitRetryAfter_ForAnExhaustedQuota_NotOnlyARateLimit()
    {
        var context = await RunAsync(new AiProviderQuotaExhaustedException("Quota exhausted.", TimeSpan.FromSeconds(90)), asAdministrator: true);

        context.Response.Headers.RetryAfter.ToString().Should().Be("90");
    }

    [Fact]
    public async Task InvokeAsync_ShouldOmitRetryAfter_WhenTheVendorSuppliedNoHint()
    {
        // FR-012: never invent one.
        var context = await RunAsync(new AiProviderRateLimitedException("Rate limited."), asAdministrator: true);

        context.Response.Headers.RetryAfter.ToString().Should().BeEmpty();
    }

    private static async Task<(JsonElement Element, int Status)> InvokeAsync(Exception exception, bool asAdministrator)
    {
        var context = await RunAsync(exception, asAdministrator);
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var element = await JsonSerializer.DeserializeAsync<JsonElement>(
            context.Response.Body, cancellationToken: TestContext.Current.CancellationToken);
        return (element, context.Response.StatusCode);
    }

    private static async Task<HttpContext> RunAsync(Exception exception, bool asAdministrator)
    {
        var middleware = new ProblemDetailsMiddleware(
            _ => throw exception, NullLogger<ProblemDetailsMiddleware>.Instance,
            NSubstitute.Substitute.For<AskLucy.Application.OperationalFailures.Abstractions.IOperationalFailureRecorder>(),
            new AskLucy.Application.OperationalFailures.FailureClassifier());

        var context = new DefaultHttpContext { Response = { Body = new MemoryStream() } };
        if (asAdministrator)
        {
            context.User = new ClaimsPrincipal(
                new ClaimsIdentity([new Claim(ClaimTypes.Role, "Administrator")], "test"));
        }

        await middleware.InvokeAsync(context);
        return context;
    }
}
