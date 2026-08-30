using AskLucy.Application.Abstractions;
using MediatR;

namespace AskLucy.Application.Ai.Queries.GetAdminAiProviders;

public sealed class GetAdminAiProvidersQueryHandler(
    IAIProviderRepository providers,
    IProviderHealthFreshnessPolicy freshnessPolicy,
    DefaultProviderResolver defaultProviderResolver)
    : IRequestHandler<GetAdminAiProvidersQuery, IReadOnlyList<AdminAiProviderDto>>
{
    public async Task<IReadOnlyList<AdminAiProviderDto>> Handle(GetAdminAiProvidersQuery request, CancellationToken cancellationToken)
    {
        var all = await providers.ListAllAsync(cancellationToken);

        // Which provider actually serves every request that has no user preference behind it —
        // location intent classification, memory extraction, any background job. Asked of the
        // resolver rather than re-derived, so the page can never disagree with the runtime.
        // Throws only in the documented zero-enabled-providers state, which is a legitimate
        // thing for this screen to display rather than fail on.
        Guid? effectivePlatformDefaultProviderId = null;
        try
        {
            var resolved = await defaultProviderResolver.ResolveAsync(preference: null, cancellationToken);
            effectivePlatformDefaultProviderId = resolved.ProviderId;
        }
        catch (InvalidOperationException)
        {
            // No enabled provider has an available model — every row simply reports false.
        }

        // specs/043 FR-019: the staleness horizon is computed server-side, where the
        // background-check interval it derives from is configured.
        return
        [
            .. all.Select(p => AdminAiProviderDto.FromEntity(
                p,
                freshnessPolicy.StaleAfterUtc(p.HealthStatusCheckedAtUtc),
                p.Id == effectivePlatformDefaultProviderId))
        ];
    }
}
