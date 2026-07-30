using AskLucy.Application.Abstractions;
using MediatR;

namespace AskLucy.Application.Consent.Queries.GetCookiePolicy;

public sealed class GetCookiePolicyQueryHandler(ICookiePolicyProvider policyProvider) : IRequestHandler<GetCookiePolicyQuery, CookiePolicyDto>
{
    public Task<CookiePolicyDto> Handle(GetCookiePolicyQuery request, CancellationToken cancellationToken)
    {
        var (version, effectiveAtUtc) = policyProvider.GetCurrentPolicy();
        return Task.FromResult(new CookiePolicyDto(version, effectiveAtUtc));
    }
}
