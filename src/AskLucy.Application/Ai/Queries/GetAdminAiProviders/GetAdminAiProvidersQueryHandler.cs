using AskLucy.Application.Abstractions;
using MediatR;

namespace AskLucy.Application.Ai.Queries.GetAdminAiProviders;

public sealed class GetAdminAiProvidersQueryHandler(IAIProviderRepository providers)
    : IRequestHandler<GetAdminAiProvidersQuery, IReadOnlyList<AdminAiProviderDto>>
{
    public async Task<IReadOnlyList<AdminAiProviderDto>> Handle(GetAdminAiProvidersQuery request, CancellationToken cancellationToken)
    {
        var all = await providers.ListAllAsync(cancellationToken);
        return [.. all.Select(AdminAiProviderDto.FromEntity)];
    }
}
