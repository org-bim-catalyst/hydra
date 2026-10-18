using AskLucy.Domain.Ai;

namespace AskLucy.Application.Abstractions;

public interface IModelDeprecationRepository
{
    void Add(ModelDeprecationBatch batch);

    Task<ModelDeprecation?> GetByModelIdAsync(Guid modelId, CancellationToken cancellationToken = default);
}
