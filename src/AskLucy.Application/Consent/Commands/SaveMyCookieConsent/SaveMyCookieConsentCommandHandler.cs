using AskLucy.Application.Abstractions;
using AskLucy.Domain.Consent;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AskLucy.Application.Consent.Commands.SaveMyCookieConsent;

internal static partial class SaveMyCookieConsentCommandHandlerLog
{
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Cookie consent recorded for {UserId}: policy {PolicyVersion}, functional={Functional}, analytics={Analytics}, marketing={Marketing}")]
    public static partial void ConsentRecorded(ILogger logger, string userId, string policyVersion, bool functional, bool analytics, bool marketing);
}

public sealed class SaveMyCookieConsentCommandHandler(
    IUserCookieConsentRepository consentRepository,
    ICookiePolicyProvider policyProvider,
    ICurrentUserAccessor currentUser,
    ILogger<SaveMyCookieConsentCommandHandler> logger)
    : IRequestHandler<SaveMyCookieConsentCommand, CookieConsentStatusDto>
{
    public async Task<CookieConsentStatusDto> Handle(SaveMyCookieConsentCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var (currentVersion, _) = policyProvider.GetCurrentPolicy();

        var record = CookieConsentRecord.Create(
            userId,
            currentVersion,
            request.Functional!.Value,
            request.Analytics!.Value,
            request.Marketing!.Value);

        await consentRepository.AddAsync(record, cancellationToken);

        // constitution §8 "log security events" — a consent change is a security/compliance-
        // relevant event (spec.md FR-016), logged via structured Serilog as an interim measure
        // (no project-wide immutable audit-trail store exists yet, research.md Topic 1).
        SaveMyCookieConsentCommandHandlerLog.ConsentRecorded(
            logger, userId, currentVersion, record.FunctionalAccepted, record.AnalyticsAccepted, record.MarketingAccepted);

        return new CookieConsentStatusDto(
            HasConsented: true,
            RequiresReconsent: false,
            PolicyVersion: currentVersion,
            CurrentPolicyVersion: currentVersion,
            Essential: true,
            Functional: record.FunctionalAccepted,
            Analytics: record.AnalyticsAccepted,
            Marketing: record.MarketingAccepted,
            LastUpdatedAtUtc: record.CreatedAtUtc);
    }
}
