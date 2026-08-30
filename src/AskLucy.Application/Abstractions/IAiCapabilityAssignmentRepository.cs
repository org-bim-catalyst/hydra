using AskLucy.Domain.Ai;

namespace AskLucy.Application.Abstractions;

public interface IAiCapabilityAssignmentRepository
{
    Task<IReadOnlyList<AiCapabilityAssignment>> ListAllAsync(CancellationToken cancellationToken = default);

    Task<AiCapabilityAssignment?> GetByCapabilityAsync(AiCapability capability, CancellationToken cancellationToken = default);

    void Add(AiCapabilityAssignment assignment);

    void Remove(AiCapabilityAssignment assignment);
}
