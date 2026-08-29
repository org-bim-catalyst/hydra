using System.Net;
using System.Security.Cryptography;
using System.Text;
using AskLucy.Application.Abstractions;
using AskLucy.Domain.Ai;
using AskLucy.Infrastructure.Ai;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AskLucy.Infrastructure.Tests.Ai;

/// <summary>
/// specs/043 SC-001/SC-008 — every row of contracts/provider-failure-classification.md §1,
/// plus the invariant that binds all of them: no classified message may carry a credential,
/// the vendor's own response body, an exception type name, or a stack trace.
/// </summary>
public sealed class AiProviderResponseClassifierTests
{
    private const string ProviderName = "Test Provider";

    // A body carrying material that must never resurface in a user-visible message: a
    // credential-shaped token and vendor prose.
    private const string LeakyBody =
        """{"error":{"message":"Bad key sk-proj-SUPERSECRET1234 supplied","type":"invalid_request_error"}}""";

    public static TheoryData<AiVendor, HttpStatusCode, string, AiProviderFailureKind> VendorCases() => new()
    {
        // ---- Google Gemini ----
        { AiVendor.GoogleGemini, HttpStatusCode.BadRequest, """{"error":{"status":"INVALID_ARGUMENT","details":[{"reason":"API_KEY_INVALID"}]}}""", AiProviderFailureKind.CredentialRejected },
        { AiVendor.GoogleGemini, HttpStatusCode.Unauthorized, """{"error":{"status":"UNAUTHENTICATED"}}""", AiProviderFailureKind.CredentialRejected },
        { AiVendor.GoogleGemini, HttpStatusCode.Forbidden, """{"error":{"status":"PERMISSION_DENIED","details":[{"reason":"SERVICE_DISABLED"}]}}""", AiProviderFailureKind.UsageRestricted },
        { AiVendor.GoogleGemini, HttpStatusCode.Forbidden, """{"error":{"status":"PERMISSION_DENIED","details":[{"reason":"BILLING_DISABLED"}]}}""", AiProviderFailureKind.UsageRestricted },
        { AiVendor.GoogleGemini, HttpStatusCode.Forbidden, """{"error":{"status":"PERMISSION_DENIED"}}""", AiProviderFailureKind.UsageRestricted },
        { AiVendor.GoogleGemini, HttpStatusCode.TooManyRequests, """{"error":{"status":"RESOURCE_EXHAUSTED","details":[{"@type":"type.googleapis.com/google.rpc.QuotaFailure","violations":[{"quotaId":"GenerateRequestsPerDayPerProject"}]}]}}""", AiProviderFailureKind.QuotaExhausted },
        { AiVendor.GoogleGemini, HttpStatusCode.TooManyRequests, """{"error":{"status":"RESOURCE_EXHAUSTED"}}""", AiProviderFailureKind.RateLimited },
        { AiVendor.GoogleGemini, HttpStatusCode.ServiceUnavailable, """{"error":{"status":"UNAVAILABLE"}}""", AiProviderFailureKind.Unavailable },
        { AiVendor.GoogleGemini, HttpStatusCode.InternalServerError, """{"error":{"status":"INTERNAL"}}""", AiProviderFailureKind.Unavailable },
        { AiVendor.GoogleGemini, HttpStatusCode.BadRequest, """{"error":{"status":"INVALID_ARGUMENT"}}""", AiProviderFailureKind.RequestInvalid },

        // ---- OpenAI ----
        { AiVendor.OpenAI, HttpStatusCode.Unauthorized, """{"error":{"code":"invalid_api_key"}}""", AiProviderFailureKind.CredentialRejected },
        { AiVendor.OpenAI, HttpStatusCode.TooManyRequests, """{"error":{"type":"insufficient_quota"}}""", AiProviderFailureKind.QuotaExhausted },
        { AiVendor.OpenAI, HttpStatusCode.TooManyRequests, """{"error":{"code":"rate_limit_exceeded"}}""", AiProviderFailureKind.RateLimited },
        { AiVendor.OpenAI, HttpStatusCode.TooManyRequests, "{}", AiProviderFailureKind.RateLimited },
        { AiVendor.OpenAI, HttpStatusCode.Forbidden, "{}", AiProviderFailureKind.CredentialRejected },
        { AiVendor.OpenAI, HttpStatusCode.BadRequest, """{"error":{"type":"invalid_request_error"}}""", AiProviderFailureKind.RequestInvalid },
        { AiVendor.OpenAI, HttpStatusCode.BadGateway, "{}", AiProviderFailureKind.Unavailable },

        // ---- Anthropic ----
        { AiVendor.Anthropic, HttpStatusCode.Unauthorized, """{"error":{"type":"authentication_error"}}""", AiProviderFailureKind.CredentialRejected },
        { AiVendor.Anthropic, HttpStatusCode.Forbidden, """{"error":{"type":"permission_error"}}""", AiProviderFailureKind.UsageRestricted },
        { AiVendor.Anthropic, HttpStatusCode.TooManyRequests, """{"error":{"type":"rate_limit_error"}}""", AiProviderFailureKind.RateLimited },
        { AiVendor.Anthropic, HttpStatusCode.BadRequest, """{"error":{"type":"invalid_request_error"}}""", AiProviderFailureKind.RequestInvalid },
        { AiVendor.Anthropic, (HttpStatusCode)529, """{"error":{"type":"overloaded_error"}}""", AiProviderFailureKind.Unavailable },

        // ---- OpenRouter ----
        { AiVendor.OpenRouter, HttpStatusCode.PaymentRequired, "{}", AiProviderFailureKind.QuotaExhausted },
        { AiVendor.OpenRouter, HttpStatusCode.Unauthorized, """{"error":{"code":"invalid_api_key"}}""", AiProviderFailureKind.CredentialRejected },
        { AiVendor.OpenRouter, HttpStatusCode.TooManyRequests, "{}", AiProviderFailureKind.RateLimited },
    };

    [Theory]
    [MemberData(nameof(VendorCases))]
    public async Task EnsureSuccessAsync_ShouldClassify_EveryDocumentedVendorCase(
        AiVendor vendor, HttpStatusCode status, string body, AiProviderFailureKind expected)
    {
        var response = Response(status, body);

        var act = () => AiProviderResponseClassifier.EnsureSuccessAsync(
            response, vendor, ProviderName, NullLogger.Instance, CancellationToken.None);

        var thrown = (await act.Should().ThrowAsync<AiProviderException>()).Which;
        thrown.Kind.Should().Be(expected);
    }

    [Theory]
    [MemberData(nameof(VendorCases))]
    public async Task EnsureSuccessAsync_ShouldNeverLeakTheVendorBody_IntoTheMessage(
        AiVendor vendor, HttpStatusCode status, string body, AiProviderFailureKind expected)
    {
        _ = expected;
        _ = body;

        // Same statuses, but with a body carrying a credential-shaped token and vendor prose.
        var response = Response(status, LeakyBody);

        var act = () => AiProviderResponseClassifier.EnsureSuccessAsync(
            response, vendor, ProviderName, NullLogger.Instance, CancellationToken.None);

        var thrown = (await act.Should().ThrowAsync<AiProviderException>()).Which;

        // SC-008. The message becomes the Problem Details `detail` an administrator reads, so
        // anything the vendor echoed back - including request material - must not survive here.
        thrown.Message.Should().NotContain("sk-proj-SUPERSECRET1234");
        thrown.Message.Should().NotContain("Bad key");
        thrown.Message.Should().NotContain("invalid_request_error");
        thrown.Message.Should().NotContain("Exception");
        thrown.Message.Should().NotContain("   at ");
    }

    [Fact]
    public async Task EnsureSuccessAsync_ShouldDoNothing_OnSuccess()
    {
        var response = Response(HttpStatusCode.OK, "{}");

        var act = () => AiProviderResponseClassifier.EnsureSuccessAsync(
            response, AiVendor.OpenAI, ProviderName, NullLogger.Instance, CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task EnsureSuccessAsync_ShouldCarryTheVendorsRetryHint_WhenSupplied()
    {
        var response = Response(HttpStatusCode.TooManyRequests, """{"error":{"type":"insufficient_quota"}}""");
        response.Headers.Add("Retry-After", "42");

        var act = () => AiProviderResponseClassifier.EnsureSuccessAsync(
            response, AiVendor.OpenAI, ProviderName, NullLogger.Instance, CancellationToken.None);

        var thrown = (await act.Should().ThrowAsync<AiProviderException>()).Which;
        thrown.RetryAfter.Should().Be(TimeSpan.FromSeconds(42));
    }

    [Fact]
    public async Task EnsureSuccessAsync_ShouldNotInventARetryHint_WhenTheVendorSuppliedNone()
    {
        // FR-012: no hint means "retry later", never a fabricated duration.
        var response = Response(HttpStatusCode.TooManyRequests, "{}");

        var act = () => AiProviderResponseClassifier.EnsureSuccessAsync(
            response, AiVendor.OpenAI, ProviderName, NullLogger.Instance, CancellationToken.None);

        var thrown = (await act.Should().ThrowAsync<AiProviderException>()).Which;
        thrown.RetryAfter.Should().BeNull();
    }

    [Fact]
    public async Task EnsureSuccessAsync_ShouldStillClassifyFromStatus_WhenTheErrorBodyIsUnparseable()
    {
        // An unparseable error body must not discard what the status already told us.
        var response = Response(HttpStatusCode.Unauthorized, "<html>gateway error</html>");

        var act = () => AiProviderResponseClassifier.EnsureSuccessAsync(
            response, AiVendor.OpenAI, ProviderName, NullLogger.Instance, CancellationToken.None);

        var thrown = (await act.Should().ThrowAsync<AiProviderException>()).Which;
        thrown.Kind.Should().Be(AiProviderFailureKind.CredentialRejected);
    }

    [Fact]
    public void ClassifyException_ShouldMapADecryptionFailure_ToCredentialUnreadable()
    {
        // FR-004 - the most likely cause of the reported symptom: a Data Protection key ring
        // replaced by a deployment, leaving a provider that reads "Configured" but cannot work.
        var classified = AiProviderResponseClassifier.ClassifyException(
            new CryptographicException("key ring"), ProviderName, CancellationToken.None);

        classified.Should().BeOfType<AiProviderCredentialUnreadableException>();
        classified!.Kind.Should().Be(AiProviderFailureKind.CredentialUnreadable);
    }

    [Fact]
    public void ClassifyException_ShouldMapAnUnrequestedCancellation_ToUnavailable()
    {
        // FR-005: nobody asked for this - it is the request timing out.
        var classified = AiProviderResponseClassifier.ClassifyException(
            new TaskCanceledException(), ProviderName, CancellationToken.None);

        classified!.Kind.Should().Be(AiProviderFailureKind.Unavailable);
    }

    [Fact]
    public void ClassifyException_ShouldReturnNull_WhenTheCallerRequestedTheCancellation()
    {
        // FR-035: a user action, not a provider failure. Returning null tells the caller to
        // rethrow it untouched.
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var classified = AiProviderResponseClassifier.ClassifyException(
            new TaskCanceledException(), ProviderName, cts.Token);

        classified.Should().BeNull();
    }

    [Theory]
    [InlineData(typeof(System.Text.Json.JsonException))]
    [InlineData(typeof(KeyNotFoundException))]
    public void ClassifyException_ShouldMapAnUninterpretableResponse_ToResponseNotUnderstood(Type exceptionType)
    {
        // FR-006. KeyNotFoundException matters especially: GetProperty on an absent member
        // throws it, and the Problem Details boundary mapped that to a confident 404 "Not found".
        var exception = (Exception)Activator.CreateInstance(exceptionType)!;

        var classified = AiProviderResponseClassifier.ClassifyException(exception, ProviderName, CancellationToken.None);

        classified!.Kind.Should().Be(AiProviderFailureKind.ResponseNotUnderstood);
    }

    [Fact]
    public void ClassifyException_ShouldLeaveAnAlreadyClassifiedFailure_Alone()
    {
        var classified = AiProviderResponseClassifier.ClassifyException(
            new AiProviderQuotaExhaustedException("already classified"), ProviderName, CancellationToken.None);

        classified.Should().BeNull();
    }

    [Fact]
    public void ClassifyException_ShouldReturnNull_ForAFailureThatIsNotTheProvidersFault()
    {
        // Anything unrecognised is rethrown by the caller rather than blamed on the provider.
        var classified = AiProviderResponseClassifier.ClassifyException(
            new NotSupportedException(), ProviderName, CancellationToken.None);

        classified.Should().BeNull();
    }

    [Fact]
    public async Task ProbeAsync_ShouldReportAProviderFailure_AsAnUnhealthyResult()
    {
        var result = await AiProviderResponseClassifier.ProbeAsync(
            _ => throw new AiProviderQuotaExhaustedException("Quota exhausted."),
            ProviderName,
            NullLogger.Instance,
            CancellationToken.None);

        result.IsHealthy.Should().BeFalse();
        result.Kind.Should().Be(AiProviderFailureKind.QuotaExhausted);
        result.Reason.Should().Be("Quota exhausted.");
    }

    [Fact]
    public async Task ProbeAsync_ShouldRethrow_WhenTheCheckMechanismItselfFails()
    {
        // FR-023: a failure that is not the provider's must never be recorded as the provider
        // being unhealthy.
        var act = () => AiProviderResponseClassifier.ProbeAsync(
            _ => throw new NotSupportedException("resolver blew up"),
            ProviderName,
            NullLogger.Instance,
            CancellationToken.None);

        await act.Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public async Task ProbeAsync_ShouldReportHealthy_WhenTheProbeSucceeds()
    {
        var result = await AiProviderResponseClassifier.ProbeAsync(
            _ => Task.CompletedTask, ProviderName, NullLogger.Instance, CancellationToken.None);

        result.IsHealthy.Should().BeTrue();
        result.Kind.Should().BeNull();
        result.Reason.Should().BeNull();
    }

    private static HttpResponseMessage Response(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}
