namespace AskLucy.Application.Abstractions;

/// <summary>One embedding provider's result for a single input (research.md Decision 5).</summary>
public sealed record EmbeddingResult(float[] Vector, int Dimensionality);

/// <summary>
/// The embedding-provider abstraction (docs/ARCHITECTURE.md &#167;13). One implementation per
/// hosting type — cloud (<c>OpenAiEmbeddingProvider</c>) or local/self-hosted
/// (<c>OnnxLocalEmbeddingProvider</c>), research.md Decision 5 — resolved at runtime by
/// <see cref="IEmbeddingServiceResolver"/> via a provider key, decoupled from the chat
/// <see cref="IAIProvider"/> a user selects (spec.md FR-006, FR-007).
/// </summary>
public interface IEmbeddingService
{
    string ProviderKey { get; }

    int Dimensionality { get; }

    Task<EmbeddingResult> EmbedAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>Batch embedding generation (FR-050) — implementations call the underlying provider's batch endpoint/loop efficiently rather than issuing N sequential single calls where avoidable.</summary>
    Task<IReadOnlyList<EmbeddingResult>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default);
}

/// <summary>Resolves an <see cref="IEmbeddingService"/> by provider key, mirroring <see cref="IAIProviderResolver"/> (FR-006, FR-007).</summary>
public interface IEmbeddingServiceResolver
{
    IEmbeddingService Resolve(string providerKey);
}
