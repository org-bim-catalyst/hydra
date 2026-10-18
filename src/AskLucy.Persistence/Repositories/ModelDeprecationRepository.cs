using AskLucy.Application.Abstractions;
using AskLucy.Domain.Ai;

namespace AskLucy.Persistence.Repositories;

public sealed class ModelDeprecationRepository(AskLucyDbContext dbContext) : IModelDeprecationRepository
{
    public void Add(ModelDeprecationBatch batch) => dbContext.ModelDeprecationBatches.Add(batch);
}
