using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Domain.KnowledgeBases;

namespace AskLucy.Infrastructure.Retrieval.VectorStores;

/// <summary>
/// The second <see cref="IVectorStore"/> implementation as of ADR-0007 — Pinecone, a managed
/// vector database, integrated via its plain REST data-plane API (not the official gRPC/Protobuf
/// SDK: every other external provider in this codebase is a JSON/HTTP integration, and pulling in
/// gRPC for one provider would be an unnecessary new dependency class per CLAUDE.md's Core Design
/// Principles). The default backing store for newly created knowledge bases; SQL Server
/// (<c>SqlServerVectorStore</c>) remains mandatory for <c>RequiresDataResidency</c> knowledge bases
/// and is the backfilled default for every knowledge base that existed before ADR-0007.
///
/// <para><b>Vector ID scheme</b>: a composite <c>"{documentChunkId:N}:{embeddingId:N}"</c>, not bare
/// <c>Embedding.Id</c> alone. Pinecone serverless indexes do not support delete-by-metadata-filter, and
/// <see cref="DeleteAsync"/> is keyed by <c>documentChunkId</c> alone — a chunk can have more than
/// one <c>Embedding</c> row over its lifetime (re-embedding on model upgrade, via
/// <c>Embedding.IsCurrent</c>). The composite id lets <see cref="DeleteAsync"/> use Pinecone's own
/// documented workaround: list-by-id-prefix, then delete-by-id.</para>
///
/// <para><b>KB scoping</b>: a metadata filter (<c>{"knowledgeBaseId":{"$in":[...]}}"</c>), not
/// Pinecone namespaces — a namespace-per-KB design would need one Pinecone round-trip per knowledge
/// base plus a merge layer inside this class, duplicating the cross-store merge
/// <c>SemanticSearchQueryHandler</c> already performs across providers. One filtered query call
/// reproduces <c>SqlServerVectorStore.QueryNearestAsync</c>'s single-call, single-ranked-result-set
/// shape.</para>
/// </summary>
public sealed class PineconeVectorStore(IHttpClientFactory httpClientFactory) : IVectorStore
{
    public VectorStoreProvider Provider => VectorStoreProvider.Pinecone;

    public async Task UpsertAsync(Guid documentChunkId, Guid embeddingId, Guid knowledgeBaseId, float[] vector, CancellationToken cancellationToken = default)
    {
        using var client = CreateClient();

        var payload = new
        {
            vectors = new[]
            {
                new
                {
                    id = VectorId(documentChunkId, embeddingId),
                    values = vector,
                    metadata = new
                    {
                        documentChunkId = documentChunkId.ToString("D", CultureInfo.InvariantCulture),
                        knowledgeBaseId = knowledgeBaseId.ToString("D", CultureInfo.InvariantCulture),
                    },
                },
            },
        };

        using var response = await client.PostAsJsonAsync("vectors/upsert", payload, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task DeleteAsync(Guid documentChunkId, CancellationToken cancellationToken = default)
    {
        using var client = CreateClient();

        var prefix = documentChunkId.ToString("N", CultureInfo.InvariantCulture) + ":";
        using var listResponse = await client.GetAsync(
            "vectors/list?prefix=" + Uri.EscapeDataString(prefix), cancellationToken);
        await EnsureSuccessAsync(listResponse, cancellationToken);

        using var listStream = await listResponse.Content.ReadAsStreamAsync(cancellationToken);
        using var listDocument = await JsonDocument.ParseAsync(listStream, cancellationToken: cancellationToken);

        if (!listDocument.RootElement.TryGetProperty("vectors", out var vectorsElement))
        {
            return;
        }

        var ids = vectorsElement.EnumerateArray()
            .Select(v => v.GetProperty("id").GetString())
            .Where(id => id is not null)
            .ToArray();

        if (ids.Length == 0)
        {
            return;
        }

        using var deleteResponse = await client.PostAsJsonAsync("vectors/delete", new { ids }, cancellationToken);
        await EnsureSuccessAsync(deleteResponse, cancellationToken);
    }

    public async Task<IReadOnlyList<VectorSearchCandidate>> QueryNearestAsync(
        float[] queryVector, IReadOnlyList<Guid> knowledgeBaseIds, int topK, double similarityThreshold,
        CancellationToken cancellationToken = default)
    {
        if (knowledgeBaseIds.Count == 0)
        {
            return [];
        }

        using var client = CreateClient();

        // Built as a Dictionary rather than an anonymous type because Pinecone's filter operator
        // key is literally "$in" — not a valid C# property name, anonymous or otherwise.
        var filter = new Dictionary<string, object>
        {
            ["knowledgeBaseId"] = new Dictionary<string, object>
            {
                ["$in"] = knowledgeBaseIds.Select(id => id.ToString("D", CultureInfo.InvariantCulture)).ToArray(),
            },
        };

        var payload = new
        {
            vector = queryVector,
            topK,
            includeMetadata = true,
            filter,
        };

        using var response = await client.PostAsJsonAsync("query", payload, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var maxDistance = 1.0 - similarityThreshold;
        var results = new List<VectorSearchCandidate>();

        if (document.RootElement.TryGetProperty("matches", out var matchesElement))
        {
            foreach (var match in matchesElement.EnumerateArray())
            {
                // Pinecone's cosine "score" is a similarity (higher = closer); this codebase's
                // Distance convention is ascending-is-better (mirrors VECTOR_DISTANCE), so convert.
                var score = match.GetProperty("score").GetDouble();
                var distance = 1.0 - score;

                if (distance > maxDistance)
                {
                    continue;
                }

                var documentChunkId = Guid.Parse(match.GetProperty("metadata").GetProperty("documentChunkId").GetString()!);
                results.Add(new VectorSearchCandidate(documentChunkId, distance));
            }
        }

        return results.OrderBy(r => r.Distance).ToList();
    }

    private HttpClient CreateClient() => httpClientFactory.CreateClient("Pinecone");

    private static string VectorId(Guid documentChunkId, Guid embeddingId) =>
        documentChunkId.ToString("N", CultureInfo.InvariantCulture) + ":" + embeddingId.ToString("N", CultureInfo.InvariantCulture);

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new AiProviderAuthenticationException($"Pinecone rejected the configured API key ({(int)response.StatusCode}).");
        }

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            var retryAfter = response.Headers.RetryAfter?.Delta;
            throw new AiProviderRateLimitedException("Pinecone rate-limited this request.", retryAfter);
        }

        throw new AiProviderUnavailableException($"Pinecone request failed with {(int)response.StatusCode}: {body}");
    }
}
