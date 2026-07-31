namespace AskLucy.Application.Abstractions;

/// <summary>
/// Resolves the <see cref="IAIProvider"/> implementation registered for a given provider key
/// (research.md Decision 3). Application depends only on this interface — the keyed-DI
/// lookup mechanics live in <c>Infrastructure</c> (constitution &#167;V, Dependency Inversion).
/// </summary>
public interface IAIProviderResolver
{
    /// <summary>Throws <see cref="KeyNotFoundException"/> if no provider is registered under <paramref name="providerKey"/> — a misconfigured/typo'd key fails loudly rather than silently.</summary>
    IAIProvider Resolve(string providerKey);
}
