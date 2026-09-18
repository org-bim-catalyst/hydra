using AskLucy.Domain.Ai;
using MediatR;

namespace AskLucy.Application.Ai.Queries.GetAiCapabilityAssignments;

public sealed record GetAiCapabilityAssignmentsQuery : IRequest<IReadOnlyList<AiCapabilityAssignmentDto>>;

/// <summary>
/// One capability and the provider serving it (and the model it pins, if any —
/// <paramref name="ModelId"/>). <paramref name="ProviderId"/> is null when nothing
/// is assigned — which is not the same as broken: the capability falls back to the platform
/// default, and <paramref name="EffectiveProviderId"/> reports where it actually lands so the
/// screen never implies a capability is unserved when it is merely unassigned.
/// </summary>
public sealed record AiCapabilityAssignmentDto(
    AiCapability Capability,
    Guid? ProviderId,
    Guid? ModelId,
    Guid? EffectiveProviderId,
    Guid? EffectiveModelId);
