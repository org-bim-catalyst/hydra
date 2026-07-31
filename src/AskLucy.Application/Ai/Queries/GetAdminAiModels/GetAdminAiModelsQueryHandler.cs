using AskLucy.Application.Abstractions;
using MediatR;

namespace AskLucy.Application.Ai.Queries.GetAdminAiModels;

public sealed class GetAdminAiModelsQueryHandler(IAIProviderRepository providers, IAIModelRepository models)
    : IRequestHandler<GetAdminAiModelsQuery, IReadOnlyList<AdminAiModelDto>>
{
    public async Task<IReadOnlyList<AdminAiModelDto>> Handle(GetAdminAiModelsQuery request, CancellationToken cancellationToken)
    {
        _ = await providers.GetByIdAsync(request.ProviderId, cancellationToken)
            ?? throw new KeyNotFoundException("Provider not found.");

        var providerModels = await models.ListByProviderIdAsync(request.ProviderId, cancellationToken);
        return [.. providerModels.Select(AdminAiModelDto.FromEntity)];
    }
}
