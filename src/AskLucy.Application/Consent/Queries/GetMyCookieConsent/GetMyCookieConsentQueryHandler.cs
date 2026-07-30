using AskLucy.Application.Abstractions;
using MediatR;

namespace AskLucy.Application.Consent.Queries.GetMyCookieConsent;

public sealed class GetMyCookieConsentQueryHandler(
    IUserCookieConsentRepository consentRepository,
    ICookiePolicyProvider policyProvider,
    ICurrentUserAccessor currentUser)
    : IRequestHandler<GetMyCookieConsentQuery, CookieConsentStatusDto>
{
    public async Task<CookieConsentStatusDto> Handle(GetMyCookieConsentQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var (currentVersion, _) = policyProvider.GetCurrentPolicy();

        var latest = await consentRepository.GetLatestAsync(userId, cancellationToken);
        if (latest is null)
        {
            return new CookieConsentStatusDto(
                HasConsented: false,
                RequiresReconsent: true,
                PolicyVersion: null,
                CurrentPolicyVersion: currentVersion,
                Essential: true,
                Functional: false,
                Analytics: false,
                Marketing: false,
                LastUpdatedAtUtc: null);
        }

        return new CookieConsentStatusDto(
            HasConsented: true,
            RequiresReconsent: latest.PolicyVersion != currentVersion,
            PolicyVersion: latest.PolicyVersion,
            CurrentPolicyVersion: currentVersion,
            Essential: true,
            Functional: latest.FunctionalAccepted,
            Analytics: latest.AnalyticsAccepted,
            Marketing: latest.MarketingAccepted,
            LastUpdatedAtUtc: latest.CreatedAtUtc);
    }
}
