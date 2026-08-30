using AskLucy.Domain.Common;

namespace AskLucy.Domain.Ai;

/// <summary>
/// Which <see cref="AIProvider"/> serves one <see cref="AiCapability"/>. At most one row per
/// capability.
/// <para>
/// Deliberately stores the provider only, never a model: the model is whichever
/// <see cref="AIProvider.DefaultModelId"/> that provider currently carries. Pinning a model here
/// too would let this row and the provider's own default disagree, and an administrator who
/// changed the provider's default would be quietly ignored for every capability assigned to it.
/// </para>
/// </summary>
public sealed class AiCapabilityAssignment : BaseEntity
{
    private AiCapabilityAssignment() { }

    public AiCapability Capability { get; private set; }

    public Guid ProviderId { get; private set; }

    public static AiCapabilityAssignment Create(AiCapability capability, Guid providerId, string actor)
    {
        if (providerId == Guid.Empty)
        {
            throw new DomainRuleViolationException("A capability assignment must name a provider.");
        }

        return new AiCapabilityAssignment
        {
            Id = Guid.CreateVersion7(),
            Capability = capability,
            ProviderId = providerId,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = actor,
        };
    }

    public void AssignTo(Guid providerId, string actor)
    {
        if (providerId == Guid.Empty)
        {
            throw new DomainRuleViolationException("A capability assignment must name a provider.");
        }

        ProviderId = providerId;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }
}
