using AskLucy.Application.Abstractions;
using AskLucy.Domain.Ai;

namespace AskLucy.Application.Ai;

/// <summary>One resolved provider/model pair, with whether it came from the caller's own saved preference or a fallback.</summary>
public sealed record ResolvedDefault(Guid ProviderId, Guid ModelId, string? GenerationParametersJson, bool IsPlatformDefault);

/// <summary>
/// FR-017/FR-019 (spec.md Assumption: "a platform-wide default provider/model exists").
/// Fallback chain: the caller's saved <see cref="UserAiPreference"/> (if its provider is
/// still enabled and its model still available) → an enabled provider's own
/// <see cref="AIProvider.DefaultModelId"/> (data-model.md: "used when a platform-wide
/// default is needed") → the first enabled provider's first available model. A
/// zero-enabled-providers state is explicitly out of scope for this spec (Clarifications
/// 2026-07-30) — <see cref="ResolveAsync"/> throws rather than returning a fabricated pair.
/// </summary>
public sealed class DefaultProviderResolver(IAIProviderRepository providers, IAIModelRepository models)
{
    public async Task<ResolvedDefault> ResolveAsync(UserAiPreference? preference, CancellationToken cancellationToken)
    {
        if (preference is { DefaultProviderId: { } preferredProviderId, DefaultModelId: { } preferredModelId })
        {
            var preferredProvider = await providers.GetByIdAsync(preferredProviderId, cancellationToken);
            var preferredModel = await models.GetByIdAsync(preferredModelId, cancellationToken);

            if (preferredProvider is { IsEnabled: true } && preferredModel is { IsSelectable: true } && preferredModel.ProviderId == preferredProviderId)
            {
                return new ResolvedDefault(preferredProviderId, preferredModelId, preference.DefaultGenerationParametersJson, IsPlatformDefault: false);
            }
        }

        var enabledProviders = await providers.ListEnabledAsync(cancellationToken);

        foreach (var provider in enabledProviders.Where(p => p.DefaultModelId.HasValue))
        {
            var defaultModel = await models.GetByIdAsync(provider.DefaultModelId!.Value, cancellationToken);
            if (defaultModel is { IsSelectable: true })
            {
                return new ResolvedDefault(provider.Id, defaultModel.Id, null, IsPlatformDefault: true);
            }
        }

        foreach (var provider in enabledProviders)
        {
            var availableModels = await models.ListAvailableByProviderIdAsync(provider.Id, cancellationToken);
            if (availableModels.Count > 0)
            {
                return new ResolvedDefault(provider.Id, availableModels[0].Id, null, IsPlatformDefault: true);
            }
        }

        throw new InvalidOperationException(
            "No enabled AI provider has an available model — production deployments are assumed to always have at least one (spec.md Assumptions).");
    }
}
