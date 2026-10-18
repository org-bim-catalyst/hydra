using AskLucy.Domain.Common;

namespace AskLucy.Domain.Ai;

/// <summary>
/// specs/069 data-model: one platform default that a retired model filled. When flagged it is the
/// attention item shown on the AI Providers page until an administrator resolves it.
/// </summary>
public sealed class PlatformDefaultChange : BaseEntity
{
    public Guid DeprecationId { get; private set; }

    public DefaultTargetKind TargetKind { get; private set; }

    public Guid ProviderId { get; private set; }

    public AiCapability? Capability { get; private set; }

    public Guid PreviousModelId { get; private set; }

    public DefaultChangeOutcome Outcome { get; private set; }

    public Guid? NewModelId { get; private set; }

    public DefaultFlagReason? FlagReason { get; private set; }

    public Guid? ServingProviderId { get; private set; }

    public Guid? ServingModelId { get; private set; }

    public string? ResolvedByUserId { get; private set; }

    public DateTime? ResolvedAtUtc { get; private set; }

    public Guid? ResolutionModelId { get; private set; }

    public bool IsOpenFlag => Outcome == DefaultChangeOutcome.Flagged && ResolvedAtUtc is null;

    private PlatformDefaultChange()
    {
    }

    public static PlatformDefaultChange Reassign(
        DefaultTargetKind targetKind,
        Guid providerId,
        AiCapability? capability,
        Guid previousModelId,
        Guid newModelId,
        string actor,
        DateTime atUtc) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            TargetKind = targetKind,
            ProviderId = providerId,
            Capability = capability,
            PreviousModelId = previousModelId,
            Outcome = DefaultChangeOutcome.Reassigned,
            NewModelId = newModelId,
            CreatedAtUtc = atUtc,
            CreatedBy = actor,
        };

    public static PlatformDefaultChange Flag(
        DefaultTargetKind targetKind,
        Guid providerId,
        AiCapability? capability,
        Guid previousModelId,
        DefaultFlagReason reason,
        Guid? servingProviderId,
        Guid? servingModelId,
        string actor,
        DateTime atUtc) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            TargetKind = targetKind,
            ProviderId = providerId,
            Capability = capability,
            PreviousModelId = previousModelId,
            Outcome = DefaultChangeOutcome.Flagged,
            FlagReason = reason,
            ServingProviderId = servingProviderId,
            ServingModelId = servingModelId,
            CreatedAtUtc = atUtc,
            CreatedBy = actor,
        };

    public void Resolve(Guid modelId, string userId, DateTime atUtc)
    {
        if (!IsOpenFlag)
        {
            throw new DomainRuleViolationException("Only an open flag can be resolved.");
        }

        ResolutionModelId = modelId;
        ResolvedByUserId = userId;
        ResolvedAtUtc = atUtc;
    }
}
