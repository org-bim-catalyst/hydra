using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai;
using Microsoft.Extensions.Options;

namespace AskLucy.Infrastructure.Ai;

/// <summary>
/// Streams synthesized speech from ElevenLabs' streaming text-to-speech endpoint, yielding
/// raw audio byte chunks as they arrive over the HTTP response — never buffering the full
/// reply before the caller sees the first bytes (FR-008/FR-026). Uses plain
/// <see cref="IHttpClientFactory"/> streaming reads, the same convention already used for
/// every other provider's SSE consumption (<see cref="OpenAIProvider"/> et al.), rather than
/// a WebSocket client (research.md Decision 3's rejected alternative).
///
/// Makes exactly one attempt per call and does not retry internally — a TTS failure
/// immediately signals the caller (<c>StreamVoiceReplyCommandHandler</c>) to record a
/// failover and fall back, per research.md Decision 5; there is no TTS-specific retry policy
/// analogous to Decision 8's STT reconnect budget.
///
/// The exact streaming endpoint path/request field names are a residual verification item
/// (research.md, "Residual verification risk" #1) — confirm against ElevenLabs' current API
/// reference before this reaches production.
/// </summary>
public sealed class ElevenLabsTextToSpeechProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<ElevenLabsOptions> options) : ITextToSpeechProvider
{
    private const int ReadBufferSize = 4096;
    private readonly ElevenLabsOptions _options = options.Value;

    public async IAsyncEnumerable<byte[]> StreamSpeechAsync(
        string textChunk,
        VoiceSettingsDto settings,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var client = CreateClient();
        var payload = new
        {
            text = textChunk,
            model_id = settings.ModelId,
            voice_settings = new
            {
                stability = settings.Stability,
                similarity_boost = settings.SimilarityBoost,
                style = settings.Style,
                speed = settings.Speed,
                use_speaker_boost = settings.UseSpeakerBoost,
            },
        };

        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"text-to-speech/{Uri.EscapeDataString(settings.VoiceId)}/stream?output_format={Uri.EscapeDataString(settings.OutputFormat)}")
        {
            Content = JsonContent.Create(payload),
        };

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (Exception ex) when (IsTransient(ex))
        {
            throw new AiProviderUnavailableException("The voice provider could not be reached.", ex);
        }

        using (response)
        {
            await EnsureSuccessAsync(response, cancellationToken);

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var buffer = new byte[ReadBufferSize];
            int bytesRead;
            while ((bytesRead = await stream.ReadAsync(buffer.AsMemory(0, ReadBufferSize), cancellationToken)) > 0)
            {
                var chunk = new byte[bytesRead];
                Array.Copy(buffer, chunk, bytesRead);
                yield return chunk;
            }
        }
    }

    public VoiceSettingsDto ResolveDefaultSettings(string language)
    {
        var voiceId = !string.IsNullOrWhiteSpace(language) && _options.VoiceIdByLanguage.TryGetValue(language, out var mapped)
            ? mapped
            : _options.VoiceId;

        return new VoiceSettingsDto(
            voiceId, _options.ModelId, _options.Stability, _options.SimilarityBoost,
            _options.Style, _options.Speed, _options.UseSpeakerBoost, _options.OutputFormat);
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


        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new AiProviderAuthenticationException($"ElevenLabs rejected the configured credential ({(int)response.StatusCode}).");
        }

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            var retryAfter = response.Headers.RetryAfter?.Delta;
            throw new AiProviderRateLimitedException("ElevenLabs rate-limited this request.", retryAfter);
        }

        // specs/043 FR-013: the vendor body must not travel in the message. Since
        // AiProviderException now carries the classification the Problem Details boundary
        // reads, an administrator sees this message verbatim - so anything the vendor echoed
        // back, which can include request material, would reach the client from here.
        throw new AiProviderUnavailableException(
            $"ElevenLabs could not process this request ({(int)response.StatusCode}).");
    }

    private static bool IsTransient(Exception ex) => ex switch
    {
        TaskCanceledException => true,
        HttpRequestException httpEx => httpEx.StatusCode is null or >= HttpStatusCode.InternalServerError,
        _ => false,
    };
}
