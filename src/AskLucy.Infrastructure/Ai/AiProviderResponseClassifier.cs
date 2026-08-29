using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Domain.Ai;
using Microsoft.Extensions.Logging;

namespace AskLucy.Infrastructure.Ai;

internal static partial class AiProviderResponseClassifierLog
{
    /// <summary>
    /// specs/043 FR-014: the vendor's own body is recorded here, server-side and truncated,
    /// and nowhere else — never in the exception message, which reaches the client.
    /// </summary>
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "{ProviderName} request failed: kind={Kind}, status={StatusCode}, vendorReason={VendorReason}, body={BodyExcerpt}")]
    public static partial void ProviderRequestFailed(
        ILogger logger, string providerName, AiProviderFailureKind kind, int statusCode, string? vendorReason, string bodyExcerpt);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{ProviderName} failure classified as {Kind}")]
    public static partial void ProviderFailureClassified(ILogger logger, Exception exception, string providerName, AiProviderFailureKind kind);
}

/// <summary>
/// Which vendor's error vocabulary to read — see contracts/provider-failure-classification.md §1.
/// Public only so it can appear in the classifier's own test signatures; the classifier itself
/// stays internal.
/// </summary>
public enum AiVendor
{
    GoogleGemini,
    OpenAI,
    Anthropic,
    OpenRouter,
}

/// <summary>
/// specs/043-provider-error-classification, research.md Decision 1 — the single place every
/// provider adapter turns a failed call into one of the nine
/// <see cref="AiProviderFailureKind"/> classifications.
///
/// Lives in Infrastructure because classifying requires reading an
/// <see cref="HttpResponseMessage"/> and the vendor's own body; the Application layer may not
/// reference <c>HttpClient</c> (constitution §3). Application sees only the resulting typed
/// exception and its <see cref="AiProviderException.Kind"/>.
///
/// Two rules this type exists to keep, and which the four divergent hand-written
/// <c>EnsureSuccessAsync</c> bodies it replaces did not:
/// <list type="number">
/// <item>The vendor's machine-readable reason wins over the HTTP status (FR-002). A
/// billing-disabled Google project and an invalid key both return 403; status alone told an
/// administrator to "check the API key" when the key was fine.</item>
/// <item>The vendor's response body is never placed in an exception message (FR-013). It is
/// logged here, truncated, and the exception carries prose built from the classification
/// alone.</item>
/// </list>
/// </summary>
internal static class AiProviderResponseClassifier
{
    private const int BodyExcerptMaxLength = 500;

    /// <summary>Throws the classified exception when <paramref name="response"/> is not a success. No-op otherwise.</summary>
    public static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        AiVendor vendor,
        string providerName,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await ReadBodySafelyAsync(response, cancellationToken);
        var vendorReason = TryReadVendorReason(vendor, body);
        var retryAfter = ReadRetryAfter(response);
        var kind = Classify(vendor, response.StatusCode, vendorReason, retryAfter);

        AiProviderResponseClassifierLog.ProviderRequestFailed(
            logger, providerName, kind, (int)response.StatusCode, vendorReason.Summary, Truncate(body));

        throw Create(kind, providerName, retryAfter);
    }

    /// <summary>
    /// Classifies a failure that never produced an HTTP response — a decryption failure, a
    /// timeout, an unparseable body (contracts/provider-failure-classification.md §1,
    /// "Non-HTTP failures"). Returns <c>null</c> when <paramref name="exception"/> is not one
    /// of ours to classify, so the caller can rethrow it untouched.
    /// </summary>
    /// <remarks>
    /// <paramref name="callerToken"/> is what separates FR-005 from FR-035: a cancellation the
    /// *caller* requested is a user action, not a provider failure, and must propagate as
    /// cancellation. Only a cancellation nobody asked for is a provider timeout.
    /// </remarks>
    public static AiProviderException? ClassifyException(Exception exception, string providerName, CancellationToken callerToken) =>
        exception switch
        {
            // Already classified upstream — leave it alone.
            AiProviderException => null,

            // FR-004. The single most likely cause of a provider that reads "Configured" while
            // every call fails: the Data Protection key ring changed under a deployment, so the
            // stored ciphertext can no longer be decrypted.
            CryptographicException => Create(AiProviderFailureKind.CredentialUnreadable, providerName, retryAfter: null, exception),

            // FR-035 takes precedence over FR-005 — see the remarks above.
            OperationCanceledException when callerToken.IsCancellationRequested => null,
            OperationCanceledException => Create(AiProviderFailureKind.Unavailable, providerName, retryAfter: null, exception),

            // FR-006. `GetProperty` on an absent member throws KeyNotFoundException, which the
            // Problem Details boundary maps to 404 "Not found" — a confidently wrong answer.
            JsonException or KeyNotFoundException or InvalidOperationException
                => Create(AiProviderFailureKind.ResponseNotUnderstood, providerName, retryAfter: null, exception),

            HttpRequestException => Create(AiProviderFailureKind.Unavailable, providerName, retryAfter: null, exception),

            _ => null,
        };

    /// <summary>
    /// Runs an operation, translating any failure it raises into a classified
    /// <see cref="AiProviderException"/> (FR-008). Wrapping the model-catalog path in this is
    /// what stops a decryption failure, a timeout, or an unparseable body reaching the API
    /// boundary untranslated and surfacing as "An unexpected error occurred."
    /// </summary>
    public static async Task<T> TranslateAsync<T>(
        Func<CancellationToken, Task<T>> operation, string providerName, CancellationToken cancellationToken)
    {
        try
        {
            return await operation(cancellationToken);
        }
        catch (Exception ex) when (ClassifyException(ex, providerName, cancellationToken) is { } classified)
        {
            throw classified;
        }
    }

    /// <summary>
    /// Runs one health probe and reports its outcome as a value (FR-016). A *provider*
    /// failure becomes an unhealthy result carrying the classification; a caller-requested
    /// cancellation propagates, and anything this classifier does not recognise is rethrown
    /// so a failure of the checking mechanism is never recorded as the provider being
    /// unhealthy (FR-023).
    /// </summary>
    public static async Task<ProviderHealthResult> ProbeAsync(
        Func<CancellationToken, Task> probe, string providerName, ILogger logger, CancellationToken cancellationToken)
    {
        try
        {
            await probe(cancellationToken);
            return new ProviderHealthResult(IsHealthy: true, Kind: null, Reason: null);
        }
        catch (Exception ex)
        {
            var classified = ex as AiProviderException ?? ClassifyException(ex, providerName, cancellationToken);
            if (classified is null)
            {
                throw;
            }

            AiProviderResponseClassifierLog.ProviderFailureClassified(logger, classified, providerName, classified.Kind);

            // The message is administrator-facing prose built from the classification alone,
            // so it is safe to persist and display verbatim (FR-013/FR-017).
            return new ProviderHealthResult(IsHealthy: false, classified.Kind, classified.Message);
        }
    }

    /// <summary>Builds the typed exception for a classification, with administrator-facing prose and never a vendor body (FR-013).</summary>
    public static AiProviderException Create(
        AiProviderFailureKind kind, string providerName, TimeSpan? retryAfter = null, Exception? inner = null) => kind switch
        {
            AiProviderFailureKind.CredentialRejected => new AiProviderAuthenticationException(
                $"{providerName} rejected the configured credential. An administrator needs to replace its API key.", inner),

            AiProviderFailureKind.CredentialUnreadable => new AiProviderCredentialUnreadableException(
                $"{providerName}'s stored credential could not be read. An administrator needs to enter it again.", inner),

            AiProviderFailureKind.NotConfigured => new AiProviderNotConfiguredException(
                $"{providerName} has no credential configured. An administrator must set one.", inner),

            AiProviderFailureKind.QuotaExhausted => new AiProviderQuotaExhaustedException(
                $"{providerName} is configured correctly, but its usage quota is exhausted. It will work again once the quota resets or is raised.",
                retryAfter, inner),

            AiProviderFailureKind.RateLimited => new AiProviderRateLimitedException(
                $"{providerName} is rate-limiting requests right now. Please try again shortly.", retryAfter, inner),

            AiProviderFailureKind.UsageRestricted => new AiProviderUsageRestrictedException(
                $"{providerName} rejected the request because the account or project is restricted — billing may be disabled, or the API may not be enabled for the project. The credential itself is valid.",
                inner),

            AiProviderFailureKind.RequestInvalid => new AiProviderRequestInvalidException(
                $"{providerName} rejected this request as invalid.", inner),

            AiProviderFailureKind.ResponseNotUnderstood => new AiProviderResponseInvalidException(
                $"{providerName} returned a response Ask Lucy could not interpret.", inner),

            _ => new AiProviderUnavailableException(
                $"{providerName} could not be reached or did not respond in time. Please try again.", inner),
        };

    /// <summary>
    /// contracts/provider-failure-classification.md §1. The precedence documented there is
    /// expressed by the order of the tests below: credential problems, then account
    /// restrictions, then quota, then rate limiting, then the generic conditions.
    /// </summary>
    private static AiProviderFailureKind Classify(
        AiVendor vendor, HttpStatusCode status, VendorReason reason, TimeSpan? retryAfter)
    {
        // OpenRouter's distinctive case: credits exhausted, reported as Payment Required.
        if (status == HttpStatusCode.PaymentRequired)
        {
            return AiProviderFailureKind.QuotaExhausted;
        }

        if (reason.IsCredentialRejected)
        {
            return AiProviderFailureKind.CredentialRejected;
        }

        if (reason.IsUsageRestricted)
        {
            return AiProviderFailureKind.UsageRestricted;
        }

        if (reason.IsQuotaExhausted)
        {
            return AiProviderFailureKind.QuotaExhausted;
        }

        if (reason.IsRateLimited || status == HttpStatusCode.TooManyRequests)
        {
            // FR-003's ambiguity rule: a 429 with no positive evidence of an exhausted
            // allowance is reported as the broader, less alarming rate-limit condition rather
            // than guessing at quota. Escalation happens above, on evidence only.
            return AiProviderFailureKind.RateLimited;
        }

        if (status is HttpStatusCode.Unauthorized)
        {
            return AiProviderFailureKind.CredentialRejected;
        }

        if (status is HttpStatusCode.Forbidden)
        {
            // Google returns 403 for an invalid key, a disabled API, and disabled billing
            // alike. With no vendor reason to separate them, "restricted" is the honest
            // answer — it points an administrator at the vendor console rather than
            // confidently blaming a credential that may be perfectly valid.
            return vendor == AiVendor.GoogleGemini
                ? AiProviderFailureKind.UsageRestricted
                : AiProviderFailureKind.CredentialRejected;
        }

        if ((int)status >= 500)
        {
            return AiProviderFailureKind.Unavailable;
        }

        return retryAfter is not null
            ? AiProviderFailureKind.RateLimited
            : AiProviderFailureKind.RequestInvalid;
    }

    private static VendorReason TryReadVendorReason(AiVendor vendor, string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return VendorReason.None;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("error", out var error) || error.ValueKind != JsonValueKind.Object)
            {
                return VendorReason.None;
            }

            return vendor == AiVendor.GoogleGemini
                ? ReadGoogleReason(error)
                : ReadOpenAiShapedReason(error);
        }
        catch (JsonException)
        {
            // An unparseable error body is not itself the classification — the status code
            // still carries information, so fall through with no vendor reason rather than
            // discarding what we do know.
            return VendorReason.None;
        }
    }

    /// <summary>Google's <c>error.status</c> plus the typed entries under <c>error.details[]</c>.</summary>
    private static VendorReason ReadGoogleReason(JsonElement error)
    {
        var status = error.TryGetProperty("status", out var statusElement) ? statusElement.GetString() : null;

        var reasons = new List<string>();
        var hasQuotaFailure = false;

        if (error.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array)
        {
            foreach (var detail in details.EnumerateArray())
            {
                if (detail.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (detail.TryGetProperty("reason", out var reasonElement) && reasonElement.GetString() is { } r)
                {
                    reasons.Add(r);
                }

                // A QuotaFailure detail is the positive evidence that separates an exhausted
                // allowance from ordinary throttling — both of which arrive as 429
                // RESOURCE_EXHAUSTED.
                if (detail.TryGetProperty("@type", out var typeElement)
                    && typeElement.GetString()?.Contains("QuotaFailure", StringComparison.Ordinal) == true)
                {
                    hasQuotaFailure = true;
                }
            }
        }

        var summary = string.Join(",", new[] { status }.Concat(reasons).Where(x => !string.IsNullOrEmpty(x)));

        return new VendorReason(
            Summary: string.IsNullOrEmpty(summary) ? null : summary,
            IsCredentialRejected:
                status is "UNAUTHENTICATED"
                || reasons.Contains("API_KEY_INVALID", StringComparer.Ordinal)
                || reasons.Contains("API_KEY_SERVICE_BLOCKED", StringComparer.Ordinal),
            IsUsageRestricted:
                reasons.Contains("SERVICE_DISABLED", StringComparer.Ordinal)
                || reasons.Contains("BILLING_DISABLED", StringComparer.Ordinal)
                || reasons.Contains("ACCESS_TOKEN_SCOPE_INSUFFICIENT", StringComparer.Ordinal)
                || (status is "PERMISSION_DENIED" && reasons.Count == 0),
            // Positive evidence only, per FR-003's ambiguity rule.
            IsQuotaExhausted: hasQuotaFailure,
            IsRateLimited: status is "RESOURCE_EXHAUSTED" && !hasQuotaFailure);
    }

    /// <summary>OpenAI's <c>error.type</c>/<c>error.code</c>; Anthropic and OpenRouter use the same envelope.</summary>
    private static VendorReason ReadOpenAiShapedReason(JsonElement error)
    {
        var type = error.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : null;
        var code = error.TryGetProperty("code", out var codeElement) && codeElement.ValueKind == JsonValueKind.String
            ? codeElement.GetString()
            : null;

        var signals = new[] { type, code }.Where(x => !string.IsNullOrEmpty(x)).ToArray();
        bool Has(params string[] candidates) => signals.Any(s => candidates.Contains(s, StringComparer.Ordinal));

        var summary = string.Join(",", signals);

        return new VendorReason(
            Summary: string.IsNullOrEmpty(summary) ? null : summary,
            IsCredentialRejected: Has("invalid_api_key", "authentication_error"),
            IsUsageRestricted: Has("permission_error", "access_terminated"),
            IsQuotaExhausted: Has("insufficient_quota", "billing_hard_limit_reached", "credit_limit_reached"),
            IsRateLimited: Has("rate_limit_exceeded", "rate_limit_error"));
    }

    private static TimeSpan? ReadRetryAfter(HttpResponseMessage response)
    {
        var header = response.Headers.RetryAfter;
        if (header is null)
        {
            return null;
        }

        if (header.Delta is { } delta)
        {
            return delta > TimeSpan.Zero ? delta : null;
        }

        if (header.Date is { } date)
        {
            var wait = date - DateTimeOffset.UtcNow;
            return wait > TimeSpan.Zero ? wait : null;
        }

        return null;
    }

    private static async Task<string> ReadBodySafelyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Failing to read the body must not mask the status code we already have.
            return string.Empty;
        }
    }

    private static string Truncate(string body) =>
        string.IsNullOrEmpty(body) ? "(empty)"
        : body.Length <= BodyExcerptMaxLength ? body
        : body[..BodyExcerptMaxLength] + "…(truncated)";

    /// <summary>What a vendor's error envelope told us, normalized across the four vocabularies.</summary>
    private readonly record struct VendorReason(
        string? Summary,
        bool IsCredentialRejected,
        bool IsUsageRestricted,
        bool IsQuotaExhausted,
        bool IsRateLimited)
    {
        public static VendorReason None => new(null, false, false, false, false);
    }
}
