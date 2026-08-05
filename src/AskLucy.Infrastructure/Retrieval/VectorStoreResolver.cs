using AskLucy.Application.Abstractions;
using AskLucy.Domain.KnowledgeBases;

namespace AskLucy.Infrastructure.Retrieval;

/// <summary>Resolves an <see cref="IVectorStore"/> by <see cref="VectorStoreProvider"/> value (ADR-0007). Depends only on <see cref="IEnumerable{IVectorStore}"/> — never on a concrete implementation — so it needs no reference to <c>AskLucy.Persistence</c> despite one implementation (<c>SqlServerVectorStore</c>) living there.</summary>
public sealed class VectorStoreResolver(IEnumerable<IVectorStore> vectorStores) : IVectorStoreResolver
{
    public IVectorStore Resolve(VectorStoreProvider provider)
    {
        var store = vectorStores.FirstOrDefault(s => s.Provider == provider);
        return store ?? throw new InvalidOperationException($"No vector store registered for provider '{provider}'.");
    }
}
