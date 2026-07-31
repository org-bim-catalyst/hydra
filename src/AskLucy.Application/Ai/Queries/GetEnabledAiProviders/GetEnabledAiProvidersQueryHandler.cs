using AskLucy.Application.Abstractions;
using MediatR;

namespace AskLucy.Application.Ai.Queries.GetEnabledAiProviders;

public sealed class GetEnabledAiProvidersQueryHandler(IAIProviderRepository providers)
    : IRequestHandler<GetEnabledAiProvidersQuery, IReadOnlyList<ProviderSummaryDto>>
{
    public async Task<IReadOnlyList<ProviderSummaryDto>> Handle(GetEnabledAiProvidersQuery request, CancellationToken cancellationToken)
    {
        var enabled = await providers.ListEnabledAsync(cancellationToken);
        return [.. enabled.Select(ProviderSummaryDto.FromEntity)];
    }
}
