using AskLucy.Application.Abstractions;
using AskLucy.Domain.Ai;

namespace AskLucy.Application.Ai.Deprecation;

/// <summary>
/// specs/069 FR-010: an open flag closes in the same save as the administrator's fix — setting a
/// provider default or a capability assignment for the function it flagged.
/// </summary>
public sealed class PlatformDefaultFlagResolver(IModelDeprecationRepository deprecations)
{
    public async Task ResolveProviderDefaultAsync(Guid providerId, Guid? modelId, string actor, CancellationToken cancellationToken = default)
    {
        if (modelId is not { } chosen)
        {
            // Clearing the default does not resolve anything: the function still has no model.
            return;
        }

        foreach (var flag in await deprecations.ListOpenFlagsAsync(providerId, capability: null, cancellationToken))
        {
            flag.Resolve(chosen, actor, DateTime.UtcNow);
        }
    }

    public async Task ResolveCapabilityAssignmentAsync(Guid providerId, AiCapability capability, Guid? modelId, string actor, CancellationToken cancellationToken = default)
    {
        if (modelId is not { } chosen)
        {
            return;
        }

        foreach (var flag in await deprecations.ListOpenFlagsAsync(providerId, capability, cancellationToken))
        {
            flag.Resolve(chosen, actor, DateTime.UtcNow);
        }
    }
}
