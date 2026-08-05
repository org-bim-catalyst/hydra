using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Domain.Retrieval;
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
public sealed class OpenAiEmbeddingProvider(IHttpClientFactory httpClientFactory, IOptions<OpenAiEmbeddingOptions> options) : IEmbeddingService
{
    private readonly OpenAiEmbeddingOptions _options = options.Value;

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

        using var client = httpClientFactory.CreateClient("OpenAiEmbedding");
        client.BaseAddress = new Uri(_options.BaseUrl);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

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
