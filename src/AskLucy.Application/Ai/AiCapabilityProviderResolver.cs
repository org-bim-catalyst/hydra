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

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "AI capability {Capability} cannot be served ({Reason}); it has no platform-default fallback")]
    public static partial void NotConfigured(ILogger logger, AiCapability capability, string reason);
}

/// <summary>
/// A capability that needs a specific kind of model (<see cref="AiCapability.ImageGeneration"/>)
/// has no usable assignment. Derives from <see cref="InvalidOperationException"/> so the admin
/// capabilities screen's existing "nothing can serve this capability" handling covers it too.
/// </summary>
public sealed class AiCapabilityNotConfiguredException(AiCapability capability, string reason)
    : InvalidOperationException($"No usable model is assigned to {capability}: {reason}. An administrator can assign one on the AI Capabilities page.")
{
    public AiCapability Capability { get; } = capability;
}

/// <summary>
/// Resolves the provider/model pair for one <see cref="AiCapability"/>: the administrator picks
/// the <b>provider</b> per capability, and the model follows from that provider's own
/// <see cref="AIProvider.DefaultModelId"/> — unless the assignment pins one
/// (<see cref="AiCapabilityAssignment.ModelId"/>), which only a capability the provider default
/// cannot serve needs.
/// <para>
/// <see cref="AiCapability.ImageGeneration"/> never falls back: the platform default is a chat
/// model, and "generating an image" with a chat model is not an imperfect provider but a broken
/// request. It throws <see cref="AiCapabilityNotConfiguredException"/> instead, so the caller can
/// tell the user plainly that image generation isn't set up.
/// </para>
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
        var requiresImageOutput = capability == AiCapability.ImageGeneration;

        var assignment = await assignments.GetByCapabilityAsync(capability, cancellationToken);
        if (assignment is null)
        {
            if (requiresImageOutput)
            {
                return NotConfigured(capability, "nothing is assigned");
            }

            AiCapabilityProviderResolverLog.NoAssignment(logger, capability);
            return await defaultProviderResolver.ResolveAsync(preference: null, cancellationToken);
        }

        var provider = await providers.GetByIdAsync(assignment.ProviderId, cancellationToken);
        if (provider is not { IsAvailableForConversation: true })
        {
            return await UnusableAsync(capability, requiresImageOutput, "the provider is missing or disabled", cancellationToken);
        }

        var modelId = assignment.ModelId ?? provider.DefaultModelId;
        if (modelId is not { } resolvedModelId)
        {
            return await UnusableAsync(capability, requiresImageOutput, "the provider has no default model set", cancellationToken);
        }

        var model = await models.GetByIdAsync(resolvedModelId, cancellationToken);
        if (model is not { IsSelectable: true } || model.ProviderId != provider.Id)
        {
            return await UnusableAsync(capability, requiresImageOutput,
                assignment.ModelId is null ? "the provider's default model is not Available" : "the assigned model is not Available", cancellationToken);
        }

        if (requiresImageOutput && !model.SupportsImageOutput)
        {
            return NotConfigured(capability, $"the assigned model '{model.ModelKey}' cannot produce images");
        }

        return new ResolvedDefault(provider.Id, model.Id, GenerationParametersJson: null, IsPlatformDefault: true);
    }

    private async Task<ResolvedDefault> UnusableAsync(
        AiCapability capability, bool requiresImageOutput, string reason, CancellationToken cancellationToken)
    {
        if (requiresImageOutput)
        {
            return NotConfigured(capability, reason);
        }

        AiCapabilityProviderResolverLog.AssignmentUnusable(logger, capability, reason);
        return await defaultProviderResolver.ResolveAsync(preference: null, cancellationToken);
    }

    private ResolvedDefault NotConfigured(AiCapability capability, string reason)
    {
        AiCapabilityProviderResolverLog.NotConfigured(logger, capability, reason);
        throw new AiCapabilityNotConfiguredException(capability, reason);
    }
}
