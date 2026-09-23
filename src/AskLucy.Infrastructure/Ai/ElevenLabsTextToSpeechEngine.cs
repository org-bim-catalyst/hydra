using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
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
/// Makes exactly one attempt per call and does not retry internally — a failure immediately
/// signals <c>VoiceProviderRouter</c> (specs/070) to try the next voice provider, and, once every
/// provider has failed, the caller to record a failover to the browser's own voice.
///
/// Authenticates with the administrator-set <c>VoiceProvider</c> credential when there is one
/// (specs/070), falling back to <see cref="ElevenLabsOptions.ApiKey"/> — the same config → DB
/// direction the chat providers took.
/// </summary>
public sealed class ElevenLabsTextToSpeechEngine(
    IHttpClientFactory httpClientFactory,
    IOptions<ElevenLabsOptions> options) : ITextToSpeechEngine
{
    public const string Key = "ElevenLabs";

    private const int ReadBufferSize = 4096;
    private readonly ElevenLabsOptions _options = options.Value;

    public string ProviderKey => Key;

    public string DisplayName => "ElevenLabs";

    public bool RequiresCredential => true;

    public async IAsyncEnumerable<byte[]> StreamSpeechAsync(
        string text,
        VoiceSettingsDto settings,
        string? apiKey,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var client = CreateClient(apiKey);
        var payload = new
        {
            text,
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

        using var response = await SendAsync(client, request, cancellationToken);

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

    public async Task<IReadOnlyList<VoiceOptionDto>> ListVoicesAsync(string? apiKey, CancellationToken cancellationToken = default)
    {
        using var client = CreateClient(apiKey);
        using var request = new HttpRequestMessage(HttpMethod.Get, "voices");
        using var response = await SendAsync(client, request, cancellationToken);

        VoicesResponse? body;
        try
        {
            body = await response.Content.ReadFromJsonAsync<VoicesResponse>(cancellationToken);
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new AiProviderResponseInvalidException("ElevenLabs returned a voice list this application could not read.", ex);
        }

        return [.. (body?.Voices ?? [])
            .Where(v => !string.IsNullOrWhiteSpace(v.VoiceId))
            .Select(v => new VoiceOptionDto(
                v.VoiceId!,
                string.IsNullOrWhiteSpace(v.Name) ? v.VoiceId! : v.Name,
                Label(v, "gender"),
                DescribeVoice(v)))];
    }

    public VoiceSettingsDto ResolveDefaultSettings(string language, string? voiceId)
    {
        var resolvedVoiceId = !string.IsNullOrWhiteSpace(voiceId)
            ? voiceId
            : !string.IsNullOrWhiteSpace(language) && _options.VoiceIdByLanguage.TryGetValue(language, out var mapped)
                ? mapped
                : _options.VoiceId;

        return new VoiceSettingsDto(
            resolvedVoiceId, _options.ModelId, _options.Stability, _options.SimilarityBoost,
            _options.Style, _options.Speed, _options.UseSpeakerBoost, _options.OutputFormat,
            language, Key);
    }

    private HttpClient CreateClient(string? apiKey)
    {
        var effectiveKey = !string.IsNullOrWhiteSpace(apiKey) ? apiKey : _options.ApiKey;
        if (string.IsNullOrWhiteSpace(effectiveKey))
        {
            throw new AiProviderNotConfiguredException("ElevenLabs has no API key configured.");
        }

        var client = httpClientFactory.CreateClient("ElevenLabs");
        client.DefaultRequestHeaders.Remove("xi-api-key");
        client.DefaultRequestHeaders.Add("xi-api-key", effectiveKey);
        return client;
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpRequestMessage request, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (Exception ex) when (IsTransient(ex))
        {
            throw new AiProviderUnavailableException("The voice provider could not be reached.", ex);
        }

        try
        {
            EnsureSuccess(response);
        }
        catch
        {
            response.Dispose();
            throw;
        }

        return response;
    }

    private static void EnsureSuccess(HttpResponseMessage response)
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

    private static string? Label(VoiceEntry voice, string name) =>
        voice.Labels is not null && voice.Labels.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;

    private static string? DescribeVoice(VoiceEntry voice)
    {
        string?[] parts = [Label(voice, "accent"), Label(voice, "age"), Label(voice, "descriptive") ?? Label(voice, "description"), voice.Category];
        var description = string.Join(", ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        return description.Length == 0 ? null : description;
    }

    private sealed record VoicesResponse([property: JsonPropertyName("voices")] IReadOnlyList<VoiceEntry>? Voices);

    private sealed record VoiceEntry(
        [property: JsonPropertyName("voice_id")] string? VoiceId,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("category")] string? Category,
        [property: JsonPropertyName("labels")] IReadOnlyDictionary<string, string>? Labels);
}
