using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Ai;
using AskLucy.Domain.Ai;
using Microsoft.Extensions.Logging;

namespace AskLucy.Infrastructure.Ai;

/// <summary>
/// ElevenLabs as an entry under Admin → AI providers — a <see cref="AIProviderKind.Speech"/>
/// vendor listed beside the language vendors for what they share: one encrypted credential, an
/// on/off switch, a health check and a model catalogue (<c>GET /v1/models</c>). It never answers
/// in conversation — the chat pickers exclude speech providers — so the chat, image and
/// transcription members refuse. Speech itself goes through
/// <see cref="ElevenLabsTextToSpeechEngine"/> and <see cref="ElevenLabsSpeechToTextSessionProvider"/>,
/// both keyed and switched from this provider's row.
/// </summary>
public sealed class ElevenLabsProvider(
    IHttpClientFactory httpClientFactory,
    IAIProviderRepository providerRepository,
    IAiCredentialProtector credentialProtector,
    ILogger<ElevenLabsProvider> logger) : IAIProvider
{
    public const string ProviderKey = "elevenlabs";

    public string ProviderName => "ElevenLabs";

    /// <summary>ElevenLabs has no chat model.</summary>
    public string ChatModel => string.Empty;

    public Task<string> ChatAsync(IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken = default) =>
        throw NotConversational();

    public Task<ChatCompletionResult> ChatAsync(
        IReadOnlyList<ChatMessage> messages, string model, GenerationParametersDto? parameters, CancellationToken cancellationToken = default) =>
        throw NotConversational();

    public IAsyncEnumerable<string> StreamChatAsync(IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken = default) =>
        throw NotConversational();

    public IAsyncEnumerable<StreamChunk> StreamChatAsync(
        IReadOnlyList<ChatMessage> messages, string model, GenerationParametersDto? parameters, CancellationToken cancellationToken = default) =>
        throw NotConversational();

    public Task<GeneratedImagePayload> GenerateImageAsync(string prompt, string model, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("ElevenLabs image generation is not supported by this provider.");

    public Task<string> TranscribeAudioAsync(Stream audioContent, string fileName, string contentType, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("ElevenLabs file transcription is not supported by this provider; live dictation uses its realtime session.");

    public Task<ProviderHealthResult> CheckHealthAsync(CancellationToken cancellationToken = default) =>
        AiProviderResponseClassifier.ProbeAsync(
            async ct =>
            {
                using var client = await CreateClientAsync(ct);
                using var response = await client.GetAsync("models", ct);
                await EnsureSuccessAsync(response, ct);
            },
            ProviderName,
            logger,
            cancellationToken);

    public Task<IReadOnlyList<ProviderModelInfo>> ListAvailableModelsAsync(CancellationToken cancellationToken = default) =>
        AiProviderResponseClassifier.TranslateAsync<IReadOnlyList<ProviderModelInfo>>(
            async ct =>
            {
                using var client = await CreateClientAsync(ct);
                using var response = await client.GetAsync("models", ct);
                await EnsureSuccessAsync(response, ct);

                using var stream = await response.Content.ReadAsStreamAsync(ct);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

                var results = new List<ProviderModelInfo>();
                foreach (var model in document.RootElement.EnumerateArray())
                {
                    var id = model.TryGetProperty("model_id", out var idElement) ? idElement.GetString() : null;
                    if (string.IsNullOrEmpty(id))
                    {
                        continue;
                    }

                    // The catalogue also lists speech-to-speech-only models; Lucy only speaks text.
                    if (model.TryGetProperty("can_do_text_to_speech", out var ttsElement) && ttsElement.ValueKind == JsonValueKind.False)
                    {
                        continue;
                    }

                    var displayName = model.TryGetProperty("name", out var nameElement) ? nameElement.GetString() ?? id : id;

                    // Token limits don't apply to a voice model; it streams audio and nothing else.
                    results.Add(new ProviderModelInfo(
                        id, displayName, ContextWindowTokens: null, MaxOutputTokens: null,
                        new AIModelCapabilities(
                            Streaming: true, Vision: false, FunctionCalling: false, JsonMode: false,
                            Reasoning: false, Embeddings: false, ImageInput: false, ImageOutput: false, Audio: true)));
                }

                return results;
            },
            ProviderName,
            cancellationToken);

    private static NotSupportedException NotConversational() =>
        new("ElevenLabs is a speech provider and cannot answer in conversation.");

    private async Task<HttpClient> CreateClientAsync(CancellationToken cancellationToken)
    {
        var provider = await providerRepository.GetByKeyAsync(ProviderKey, cancellationToken)
            ?? throw new AiProviderNotConfiguredException("ElevenLabs is not configured in the provider catalog.");

        if (provider.CredentialCiphertext is null)
        {
            throw new AiProviderNotConfiguredException("ElevenLabs has no credential configured. An administrator must set one.");
        }

        // specs/043 FR-004 - see GoogleGeminiProvider.CreateClientAsync for why this matters.
        string apiKey;
        try
        {
            apiKey = credentialProtector.Unprotect(provider.CredentialCiphertext);
        }
        catch (System.Security.Cryptography.CryptographicException ex)
        {
            throw AiProviderResponseClassifier.Create(AiProviderFailureKind.CredentialUnreadable, ProviderName, retryAfter: null, ex);
        }

        // The named client already carries ElevenLabs' base address (DependencyInjection).
        var client = httpClientFactory.CreateClient("ElevenLabs");
        client.DefaultRequestHeaders.Remove("xi-api-key");
        client.DefaultRequestHeaders.Add("xi-api-key", apiKey);
        return client;
    }

    /// <summary>specs/043: classification is delegated to the one shared table rather than re-derived from the status code here.</summary>
    private Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken) =>
        AiProviderResponseClassifier.EnsureSuccessAsync(response, AiVendor.ElevenLabs, ProviderName, logger, cancellationToken);
}
