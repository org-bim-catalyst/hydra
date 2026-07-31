using AskLucy.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace AskLucy.Infrastructure.Ai;

/// <summary>Keyed-DI lookup (research.md Decision 3) — the only place in the codebase that resolves an <see cref="IAIProvider"/> by a runtime string key rather than by concrete type.</summary>
public sealed class AiProviderResolver(IServiceProvider serviceProvider) : IAIProviderResolver
{
    public IAIProvider Resolve(string providerKey)
    {
        try
        {
            return serviceProvider.GetRequiredKeyedService<IAIProvider>(providerKey);
        }
        catch (InvalidOperationException ex)
        {
            throw new KeyNotFoundException($"No AI provider is registered under key '{providerKey}'.", ex);
        }
    }
}
