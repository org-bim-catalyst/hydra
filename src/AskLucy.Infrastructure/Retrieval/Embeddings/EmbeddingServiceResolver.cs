using AskLucy.Application.Abstractions;

namespace AskLucy.Infrastructure.Retrieval.Embeddings;

/// <summary>
/// Resolves an <see cref="IEmbeddingService"/> by provider key, mirroring
/// <see cref="IAIProviderResolver"/>'s runtime-keyed-resolution shape (spec.md FR-006, FR-007).
/// </summary>
public sealed class EmbeddingServiceResolver(IEnumerable<IEmbeddingService> services) : IEmbeddingServiceResolver
{
    public IEmbeddingService Resolve(string providerKey)
    {
        var service = services.FirstOrDefault(s => string.Equals(s.ProviderKey, providerKey, StringComparison.OrdinalIgnoreCase));
        return service ?? throw new AiProviderUnavailableException($"No embedding provider registered for key '{providerKey}'.");
    }
}
