using AskLucy.Application.Abstractions;
using AskLucy.Domain.Ai;
using Microsoft.Extensions.Logging;

namespace AskLucy.Application.Ai;

internal static partial class AiCapabilityProviderResolverLog
{
    [LoggerMessage(Level = LogLevel.Warning,
        Message = "AI capability {Capability} is assigned to a provider that cannot serve it ({Reason}); falling back to the platform default")]
    public static partial void AssignmentUnusable(ILogger logger, AiCapability capability, string reason);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "AI capability {Capability} has no provider assigned; falling back to the platform default")]
    public static partial void NoAssignment(ILogger logger, AiCapability capability);
}

/// <summary>
/// Resolves the provider/model pair for one <see cref="AiCapability"/>: the administrator picks
/// the <b>provider</b> per capability, and the model follows from that provider's own
/// <see cref="AIProvider.DefaultModelId"/>. Two settings, one decision each — never a model
/// pinned per capability that could drift from the provider's default.
/// <para>
/// Replaces the previous behaviour for these call sites, which was
/// <c>DefaultProviderResolver.ResolveAsync(preference: null)</c> — whose last resort is "first
/// enabled provider in display-name order". That is not a choice anyone made, and it routed
/// location intent classification to a provider whose credit had run out while the operator's
/// own chat ran fine on another.
/// </para>
/// <para>
/// Falls back to <see cref="DefaultProviderResolver"/> rather than throwing when a capability is
/// unassigned or its assignment has become unusable (provider disabled, default model cleared or
/// no longer Available). A capability quietly doing nothing is worse than one running on an
/// imperfect provider — but the fallback is always logged, because silently reverting to the
/// alphabetical rule is exactly the failure this class exists to end.
/// </para>
/// </summary>
public sealed class AiCapabilityProviderResolver(
    IAiCapabilityAssignmentRepository assignments,
    IAIProviderRepository providers,
    IAIModelRepository models,
    DefaultProviderResolver defaultProviderResolver,
    ILogger<AiCapabilityProviderResolver> logger)
{
    public async Task<ResolvedDefault> ResolveAsync(AiCapability capability, CancellationToken cancellationToken)
    {
        var assignment = await assignments.GetByCapabilityAsync(capability, cancellationToken);
        if (assignment is null)
        {
            AiCapabilityProviderResolverLog.NoAssignment(logger, capability);
            return await defaultProviderResolver.ResolveAsync(preference: null, cancellationToken);
        }

        var provider = await providers.GetByIdAsync(assignment.ProviderId, cancellationToken);
        if (provider is null || !provider.IsEnabled)
        {
            AiCapabilityProviderResolverLog.AssignmentUnusable(logger, capability, "the provider is missing or disabled");
            return await defaultProviderResolver.ResolveAsync(preference: null, cancellationToken);
        }

        if (provider.DefaultModelId is not { } defaultModelId)
        {
            AiCapabilityProviderResolverLog.AssignmentUnusable(logger, capability, "the provider has no default model set");
            return await defaultProviderResolver.ResolveAsync(preference: null, cancellationToken);
        }

        var model = await models.GetByIdAsync(defaultModelId, cancellationToken);
        if (model is not { IsSelectable: true })
        {
            AiCapabilityProviderResolverLog.AssignmentUnusable(logger, capability, "the provider's default model is not Available");
            return await defaultProviderResolver.ResolveAsync(preference: null, cancellationToken);
        }

        return new ResolvedDefault(provider.Id, model.Id, GenerationParametersJson: null, IsPlatformDefault: true);
    }
}
