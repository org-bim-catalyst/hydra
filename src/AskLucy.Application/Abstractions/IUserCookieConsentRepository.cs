using AskLucy.Domain.Consent;

namespace AskLucy.Application.Abstractions;

/// <summary>
/// Append-only access to a user's cookie-consent decisions (specs/004-cookie-consent-privacy,
/// data-model.md). There is no update/delete method — every preference change is a new
/// inserted row (research.md Topic 2).
/// </summary>
public interface IUserCookieConsentRepository
{
    /// <summary>The most recently recorded decision for the user, or <c>null</c> if none exists yet.</summary>
    Task<CookieConsentRecord?> GetLatestAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>Every decision ever recorded for the user, ordered by <c>CreatedAtUtc</c> descending — answers FR-016's "what was this user's consent state on date X."</summary>
    Task<IReadOnlyList<CookieConsentRecord>> GetHistoryAsync(string userId, CancellationToken cancellationToken = default);

    Task AddAsync(CookieConsentRecord record, CancellationToken cancellationToken = default);
}
