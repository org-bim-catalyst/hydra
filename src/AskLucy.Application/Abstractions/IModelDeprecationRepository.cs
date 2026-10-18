using AskLucy.Domain.Ai;

namespace AskLucy.Application.Abstractions;

public interface IModelDeprecationRepository
{
    void Add(ModelDeprecationBatch batch);

    Task<ModelDeprecation?> GetByModelIdAsync(Guid modelId, CancellationToken cancellationToken = default);

    /// <summary>The batch with its deprecations (and their replacements) loaded, for the user-impact job.</summary>
    Task<ModelDeprecationBatch?> GetBatchAsync(Guid batchId, CancellationToken cancellationToken = default);

    /// <summary>Batches still Pending after <paramref name="confirmedBeforeUtc"/>, for the sweep.</summary>
    Task<IReadOnlyList<Guid>> ListStuckPendingBatchIdsAsync(DateTime confirmedBeforeUtc, CancellationToken cancellationToken = default);
}
