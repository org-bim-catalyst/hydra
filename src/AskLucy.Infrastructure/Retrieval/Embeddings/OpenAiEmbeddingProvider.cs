using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Domain.Ai;
using AskLucy.Domain.Retrieval;
using AskLucy.Infrastructure.Ai;
using Microsoft.Extensions.Options;

namespace AskLucy.Infrastructure.Retrieval.Embeddings;

/// <summary>
/// The cloud-hosted <see cref="IEmbeddingService"/> (research.md Decision 5) — calls OpenAI's
/// embeddings endpoint. OpenAI is the only one of the platform's existing chat providers
/// (OpenAI/Anthropic/Google Gemini/OpenRouter) offering a first-class embeddings API, making it
/// the default requiring no new vendor-integration pattern (spec.md Assumptions). Never used for
/// a knowledge base with <c>RequiresDataResidency</c> set (FR-009a) — that validation happens in
/// <c>KnowledgeBase.UpdateRetrievalSettings</c>, not here. Failures surface through the same
/// exception types <see cref="AskLucy.Application.Abstractions.IAIProvider"/> implementations use
/// (FR-009, constitution §2.VIII).
/// </summary>
public sealed class OpenAiEmbeddingProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<OpenAiEmbeddingOptions> options,
    IAIProviderRepository providerRepository,
    IAiCredentialProtector credentialProtector) : IEmbeddingService
{
    private readonly OpenAiEmbeddingOptions _options = options.Value;

    /// <summary>
    /// The key this provider authenticates with: the one an administrator configured for OpenAI
    /// on the AI providers page, falling back to <c>OpenAiEmbedding:ApiKey</c> from configuration.
    /// </summary>
    /// <remarks>
    /// It used to read configuration only. That section is set in neither appsettings.json nor
    /// appsettings.Production.json, so in production the key was empty and every embedding call
    /// returned 401 — memory retrieval failed silently on every single turn while chat, reading
    /// the same vendor's credential from the database, worked fine. Configuring OpenAI in the
    /// admin UI is the thing an administrator actually does, so that credential is what this
    /// uses; the configuration value stays as an override for environments that set it.
    /// </remarks>
    private async Task<string> ResolveApiKeyAsync(CancellationToken cancellationToken)
    {
        var provider = await providerRepository.GetByKeyAsync("openai", cancellationToken);
        if (provider?.CredentialCiphertext is not null)
        {
            try
            {
                return credentialProtector.Unprotect(provider.CredentialCiphertext);
            }
            catch (System.Security.Cryptography.CryptographicException ex)
            {
                // An unreadable credential is a real fault worth naming, not a reason to fall
                // back to a key that is almost certainly absent.
                throw AiProviderResponseClassifier.Create(
                    AiProviderFailureKind.CredentialUnreadable, "OpenAI", retryAfter: null, ex);
            }
        }

        return _options.ApiKey;
    }

    public string ProviderKey => "OpenAI";

    public int Dimensionality => Embedding.VectorWidth;

    public async Task<EmbeddingResult> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        var results = await EmbedBatchAsync([text], cancellationToken);
        return results[0];
    }

    public async Task<IReadOnlyList<EmbeddingResult>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default)
    {
        if (texts.Count == 0)
        {
            return [];
        }

        var apiKey = await ResolveApiKeyAsync(cancellationToken);

        using var client = httpClientFactory.CreateClient("OpenAiEmbedding");
        client.BaseAddress = new Uri(_options.BaseUrl);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var payload = new
        {
            model = _options.Model,
            input = texts,
        };

        using var response = await client.PostAsJsonAsync("embeddings", payload, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var results = new List<EmbeddingResult>(texts.Count);
        foreach (var item in document.RootElement.GetProperty("data").EnumerateArray())
        {
            var vector = item.GetProperty("embedding").EnumerateArray().Select(v => (float)v.GetDouble()).ToArray();
            results.Add(new EmbeddingResult(EmbeddingVectorProjection.ProjectToSharedWidth(vector), Embedding.VectorWidth));
        }

        return results;
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
            throw new AiProviderAuthenticationException($"OpenAI rejected the configured embedding credential ({(int)response.StatusCode}).");
        }

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            var retryAfter = response.Headers.RetryAfter?.Delta;
            throw new AiProviderRateLimitedException("OpenAI rate-limited this embedding request.", retryAfter);
        }

        throw new AiProviderUnavailableException($"OpenAI embedding request failed with {(int)response.StatusCode}: {body}");
    }
}
