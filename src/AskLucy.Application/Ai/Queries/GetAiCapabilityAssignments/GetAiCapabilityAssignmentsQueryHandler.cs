using AskLucy.Application.Abstractions;
using AskLucy.Domain.Ai;
using MediatR;

namespace AskLucy.Application.Ai.Queries.GetAiCapabilityAssignments;

public sealed class GetAiCapabilityAssignmentsQueryHandler(
    IAiCapabilityAssignmentRepository assignments,
    AiCapabilityProviderResolver capabilityProviderResolver)
    : IRequestHandler<GetAiCapabilityAssignmentsQuery, IReadOnlyList<AiCapabilityAssignmentDto>>
{
    public async Task<IReadOnlyList<AiCapabilityAssignmentDto>> Handle(
        GetAiCapabilityAssignmentsQuery request, CancellationToken cancellationToken)
    {
        var stored = (await assignments.ListAllAsync(cancellationToken)).ToDictionary(a => a.Capability);

        var results = new List<AiCapabilityAssignmentDto>();
        foreach (var capability in Enum.GetValues<AiCapability>())
        {
            stored.TryGetValue(capability, out var assignment);

            // Asked of the same resolver the runtime uses, so the screen can never disagree with
            // it. A capability with no assignment still resolves — to the platform default — and
            // showing that is the difference between "unassigned" and "not working".
            Guid? effectiveProviderId = null;
            Guid? effectiveModelId = null;
            try
            {
                var resolved = await capabilityProviderResolver.ResolveAsync(capability, cancellationToken);
                effectiveProviderId = resolved.ProviderId;
                effectiveModelId = resolved.ModelId;
            }
            catch (InvalidOperationException)
            {
                // The documented zero-enabled-providers state: nothing can serve this capability
                // yet. Rendered as such rather than failing the whole screen.
            }

            results.Add(new AiCapabilityAssignmentDto(
                capability, assignment?.ProviderId, effectiveProviderId, effectiveModelId));
        }

        return results;
    }
}
