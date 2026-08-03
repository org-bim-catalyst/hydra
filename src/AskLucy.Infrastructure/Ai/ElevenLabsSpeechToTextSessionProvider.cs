using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AskLucy.Application.Abstractions;
using Microsoft.Extensions.Options;

namespace AskLucy.Infrastructure.Ai;

/// <summary>
/// Mints a single-use, short-lived ElevenLabs realtime speech-to-text token server-side
/// (research.md Decision 2) — the browser connects directly to ElevenLabs with this token
/// afterward, never with <see cref="ElevenLabsOptions.ApiKey"/> itself.
///
/// Deliberately makes exactly one attempt per call and does not retry internally: the bounded
/// reconnect/retry policy (research.md Decision 8) lives client-side in
/// <c>useSpeechRecognition.ts</c>, which calls <c>CreateSpeechToTextSessionCommand</c> again
/// on failure — retrying here too would double the retry budget in a way neither layer could
/// see.
///
/// The exact token-mint endpoint path/response field names are a residual verification item
/// (research.md, "Residual verification risk" #1) — the ElevenLabs API reference pages for
/// this specific endpoint returned 404 during planning-time research. <see cref="TokenMintPath"/>
/// and the response field names below MUST be confirmed against ElevenLabs' current API
/// reference before this reaches production.
/// </summary>
public sealed class ElevenLabsSpeechToTextSessionProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<ElevenLabsOptions> options) : ISpeechToTextSessionProvider
{
    private const string TokenMintPath = "speech-to-text/realtime/token";

    private readonly ElevenLabsOptions _options = options.Value;

    public async Task<SpeechToTextSession> CreateSessionAsync(string language, CancellationToken cancellationToken = default)
    {
        using var client = CreateClient();
        var payload = new Dictionary<string, object?>
        {
            ["model_id"] = _options.ModelId,
            ["language_code"] = string.IsNullOrWhiteSpace(language) ? null : language,
        };

        HttpResponseMessage response;
        try
        {
            response = await client.PostAsJsonAsync(TokenMintPath, payload, cancellationToken);
        }
        catch (Exception ex) when (IsTransient(ex))
        {
            throw new AiProviderUnavailableException("The voice provider could not be reached.", ex);
        }

        using (response)
        {
            await EnsureSuccessAsync(response, cancellationToken);

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            var token = document.RootElement.GetProperty("token").GetString()
                ?? throw new AiProviderUnavailableException("The voice provider returned no session token.");

            // ElevenLabs-documented lifetime is ~15 minutes; if the response reports its own
            // expiry, prefer that over the assumed default.
            var expiresAtUtc = document.RootElement.TryGetProperty("expires_at", out var expiresElement)
                && expiresElement.TryGetDateTime(out var parsedExpiry)
                    ? parsedExpiry
                    : DateTime.UtcNow.AddMinutes(15);

            return new SpeechToTextSession(token, expiresAtUtc);
        }
    }

    private HttpClient CreateClient()
    {
        var client = httpClientFactory.CreateClient("ElevenLabs");
        client.DefaultRequestHeaders.Remove("xi-api-key");
        client.DefaultRequestHeaders.Add("xi-api-key", _options.ApiKey);
        return client;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new AiProviderAuthenticationException($"ElevenLabs rejected the configured credential ({(int)response.StatusCode}).");
        }

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            var retryAfter = response.Headers.RetryAfter?.Delta;
            throw new AiProviderRateLimitedException("ElevenLabs rate-limited this request.", retryAfter);
        }

        throw new AiProviderUnavailableException(
            $"ElevenLabs request failed with {(int)response.StatusCode}: {body}");
    }

    private static bool IsTransient(Exception ex) => ex switch
    {
        TaskCanceledException => true,
        HttpRequestException httpEx => httpEx.StatusCode is null or >= HttpStatusCode.InternalServerError,
        _ => false,
    };
}
