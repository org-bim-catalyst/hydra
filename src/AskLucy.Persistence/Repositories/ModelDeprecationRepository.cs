using AskLucy.Application.Abstractions;
using AskLucy.Domain.Ai;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Repositories;

public sealed class ModelDeprecationRepository(AskLucyDbContext dbContext) : IModelDeprecationRepository
{
    public void Add(ModelDeprecationBatch batch) => dbContext.ModelDeprecationBatches.Add(batch);

    public Task<ModelDeprecation?> GetByModelIdAsync(Guid modelId, CancellationToken cancellationToken = default) =>
        dbContext.ModelDeprecations.FirstOrDefaultAsync(d => d.ModelId == modelId, cancellationToken);
}
