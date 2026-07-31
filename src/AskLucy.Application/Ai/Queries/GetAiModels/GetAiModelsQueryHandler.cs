using AskLucy.Application.Abstractions;
using MediatR;

namespace AskLucy.Application.Ai.Queries.GetAiModels;

public sealed class GetAiModelsQueryHandler(IAIModelRepository models, IAIProviderRepository providers)
    : IRequestHandler<GetAiModelsQuery, IReadOnlyList<ModelSummaryDto>>
{
    public async Task<IReadOnlyList<ModelSummaryDto>> Handle(GetAiModelsQuery request, CancellationToken cancellationToken)
    {
        if (request.ProviderId is { } providerId)
        {
            var provider = await providers.GetByIdAsync(providerId, cancellationToken);
            if (provider is null || !provider.IsEnabled)
            {
                throw new KeyNotFoundException("Provider not found.");
            }

            var providerModels = await models.ListAvailableByProviderIdAsync(providerId, cancellationToken);
            return [.. providerModels.Select(m => ModelSummaryDto.FromEntity(m, provider))];
        }

        var allProviders = (await providers.ListAllAsync(cancellationToken)).ToDictionary(p => p.Id);
        var availableModels = await models.ListAvailableAsync(cancellationToken);

        return [.. availableModels
            .Where(m => allProviders.ContainsKey(m.ProviderId))
            .Select(m => ModelSummaryDto.FromEntity(m, allProviders[m.ProviderId]))];
    }
}
