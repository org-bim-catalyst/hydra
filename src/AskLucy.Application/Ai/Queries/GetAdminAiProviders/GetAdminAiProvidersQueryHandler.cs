using AskLucy.Application.Abstractions;
using MediatR;

namespace AskLucy.Application.Ai.Queries.GetAdminAiProviders;

public sealed class GetAdminAiProvidersQueryHandler(
    IAIProviderRepository providers,
    IProviderHealthFreshnessPolicy freshnessPolicy)
    : IRequestHandler<GetAdminAiProvidersQuery, IReadOnlyList<AdminAiProviderDto>>
{
    public async Task<IReadOnlyList<AdminAiProviderDto>> Handle(GetAdminAiProvidersQuery request, CancellationToken cancellationToken)
    {
        var all = await providers.ListAllAsync(cancellationToken);

        // specs/043 FR-019: the staleness horizon is computed server-side, where the
        // background-check interval it derives from is configured.
        return [.. all.Select(p => AdminAiProviderDto.FromEntity(p, freshnessPolicy.StaleAfterUtc(p.HealthStatusCheckedAtUtc)))];
    }
}
