using AskLucy.Application.Abstractions;
using AskLucy.Domain.Ai;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Repositories;

public sealed class AiCapabilityAssignmentRepository(AskLucyDbContext dbContext) : IAiCapabilityAssignmentRepository
{
    public async Task<IReadOnlyList<AiCapabilityAssignment>> ListAllAsync(CancellationToken cancellationToken = default) =>
        await dbContext.AiCapabilityAssignments.OrderBy(a => a.Capability).ToListAsync(cancellationToken);

    public async Task<AiCapabilityAssignment?> GetByCapabilityAsync(AiCapability capability, CancellationToken cancellationToken = default) =>
        await dbContext.AiCapabilityAssignments.FirstOrDefaultAsync(a => a.Capability == capability, cancellationToken);

    public void Add(AiCapabilityAssignment assignment) => dbContext.AiCapabilityAssignments.Add(assignment);

    public void Remove(AiCapabilityAssignment assignment) => dbContext.AiCapabilityAssignments.Remove(assignment);
}
