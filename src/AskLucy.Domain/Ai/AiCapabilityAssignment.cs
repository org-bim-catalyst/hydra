using AskLucy.Domain.Common;

namespace AskLucy.Domain.Ai;

/// <summary>
/// Which <see cref="AIProvider"/> — and, optionally, which of its models — serves one
/// <see cref="AiCapability"/>. At most one row per capability.
/// <para>
/// <see cref="ModelId"/> is null by default, and null means "whichever
/// <see cref="AIProvider.DefaultModelId"/> that provider currently carries": pinning a model when
/// none is needed would let this row and the provider's own default disagree, and an
/// administrator who changed the provider's default would be quietly ignored for every capability
/// assigned to it. A model is pinned only where the default cannot serve at all —
/// <see cref="AiCapability.ImageGeneration"/>, whose provider default is a chat model.
/// </para>
/// </summary>
public sealed class AiCapabilityAssignment : BaseEntity
{
    private AiCapabilityAssignment() { }

    public AiCapability Capability { get; private set; }

    public Guid ProviderId { get; private set; }

    /// <summary>A specific model of <see cref="ProviderId"/>, or null to follow that provider's current default.</summary>
    public Guid? ModelId { get; private set; }

    public static AiCapabilityAssignment Create(AiCapability capability, Guid providerId, Guid? modelId, string actor)
    {
        EnsureValid(capability, providerId, modelId);

        return new AiCapabilityAssignment
        {
            Id = Guid.CreateVersion7(),
            Capability = capability,
            ProviderId = providerId,
            ModelId = modelId,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = actor,
        };
    }

    public void AssignTo(Guid providerId, Guid? modelId, string actor)
    {
        EnsureValid(Capability, providerId, modelId);

        ProviderId = providerId;
        ModelId = modelId;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    private static void EnsureValid(AiCapability capability, Guid providerId, Guid? modelId)
    {
        if (providerId == Guid.Empty)
        {
            throw new DomainRuleViolationException("A capability assignment must name a provider.");
        }

        if (modelId == Guid.Empty)
        {
            throw new DomainRuleViolationException("A pinned model must be a real model id.");
        }

        if (capability == AiCapability.ImageGeneration && modelId is null)
        {
            throw new DomainRuleViolationException("Image generation must name an image-capable model; a provider's default model is a chat model.");
        }
    }
}
