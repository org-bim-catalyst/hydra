using AskLucy.Application.Abstractions;
using AskLucy.Domain.Ai;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Repositories;

public sealed class ModelDeprecationRepository(AskLucyDbContext dbContext) : IModelDeprecationRepository
{
    public void Add(ModelDeprecationBatch batch) => dbContext.ModelDeprecationBatches.Add(batch);

    public Task<ModelDeprecation?> GetByModelIdAsync(Guid modelId, CancellationToken cancellationToken = default) =>
        dbContext.ModelDeprecations.FirstOrDefaultAsync(d => d.ModelId == modelId, cancellationToken);

    public Task<ModelDeprecationBatch?> GetBatchAsync(Guid batchId, CancellationToken cancellationToken = default) =>
        dbContext.ModelDeprecationBatches
            .Include(b => b.Deprecations)
            .FirstOrDefaultAsync(b => b.Id == batchId, cancellationToken);

    public async Task<IReadOnlyList<Guid>> ListStuckPendingBatchIdsAsync(DateTime confirmedBeforeUtc, CancellationToken cancellationToken = default) =>
        await dbContext.ModelDeprecationBatches
            .Where(b => b.UserImpactStatus == UserImpactStatus.Pending && b.ConfirmedAtUtc < confirmedBeforeUtc)
            .Select(b => b.Id)
            .ToListAsync(cancellationToken);
}
