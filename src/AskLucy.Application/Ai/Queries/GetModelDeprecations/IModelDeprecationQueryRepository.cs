namespace AskLucy.Application.Ai.Queries.GetModelDeprecations;

public interface IModelDeprecationQueryRepository
{
    Task<ModelDeprecationPageDto> ListAsync(
        Guid? providerId,
        bool awaitingValidation,
        bool openFlags,
        Guid? cursor,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<ModelDeprecationDetailDto?> GetAsync(Guid deprecationId, CancellationToken cancellationToken = default);
}
