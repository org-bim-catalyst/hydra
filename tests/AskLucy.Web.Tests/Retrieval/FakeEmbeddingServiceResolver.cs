using AskLucy.Application.Abstractions;

namespace AskLucy.Web.Tests.Retrieval;

/// <summary>
/// T236 — the one fake tasks.md names explicitly: stands in for whichever embedding vendor a
/// knowledge base's <c>EmbeddingProvider</c> row names, resolved by <c>IndexingOrchestrator</c> via
/// <see cref="Resolve"/>. Returns the same <see cref="FakeEmbeddingService"/> regardless of the key
/// requested, so a test only has to control one thing (<see cref="ShouldFail"/>) to drive both the
/// success and failure paths of <c>KnowledgeBaseIndexingJob</c>.
/// </summary>
public sealed class FakeEmbeddingServiceResolver : IEmbeddingServiceResolver
{
    private readonly FakeEmbeddingService _service = new();

    public bool ShouldFail
    {
        get => _service.ShouldFail;
        set => _service.ShouldFail = value;
    }

    public void Reset() => _service.ShouldFail = false;

    public IEmbeddingService Resolve(string providerKey) => _service;
}

/// <summary>
/// A deterministic vector per input, of <see cref="AskLucy.Domain.Retrieval.Embedding.VectorWidth"/>
/// length (the SQL Server <c>vector(n)</c> column's fixed width — see
/// <c>SqlServerVectorStore.UpsertAsync</c>'s own doc comment), so a real <c>UPDATE ... CAST(@json AS
/// VECTOR(1536))</c> round-trips against the real database without a live embedding vendor call.
/// </summary>
public sealed class FakeEmbeddingService : IEmbeddingService
{
    public string ProviderKey => "fake";

    public int Dimensionality => AskLucy.Domain.Retrieval.Embedding.VectorWidth;

    public bool ShouldFail { get; set; }

    public Task<EmbeddingResult> EmbedAsync(string text, CancellationToken cancellationToken = default) =>
        ShouldFail
            ? throw new InvalidOperationException("The embedding vendor rejected the request (fake, T236 failure path).")
            : Task.FromResult(new EmbeddingResult(BuildVector(text), Dimensionality));

    public Task<IReadOnlyList<EmbeddingResult>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default) =>
        ShouldFail
            ? throw new InvalidOperationException("The embedding vendor rejected the request (fake, T236 failure path).")
            : Task.FromResult<IReadOnlyList<EmbeddingResult>>(
                texts.Select(t => new EmbeddingResult(BuildVector(t), Dimensionality)).ToList());

    private float[] BuildVector(string text)
    {
        var vector = new float[Dimensionality];
        var seed = text.Length == 0 ? 1 : text.Length;
        for (var i = 0; i < vector.Length; i++)
        {
            vector[i] = (float)((i + seed) % 97) / 97f;
        }

        return vector;
    }
}
