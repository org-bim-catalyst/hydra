using AskLucy.Domain.Common;

namespace AskLucy.Domain.Consent;

/// <summary>
/// A single, immutable cookie-consent decision (specs/004-cookie-consent-privacy).
/// Append-only by design (research.md Topic 2): a preference change is a new inserted
/// row, never a mutation of an existing one. "Current state" for a user is the row with
/// the latest <see cref="BaseEntity.CreatedAtUtc"/>; the full row history answers "what was
/// this user's consent state on date X" (FR-016) without a separate audit table.
/// </summary>
public sealed class CookieConsentRecord : BaseEntity
{
    public string UserId { get; private set; } = string.Empty;

    /// <summary>The cookie/privacy policy version this decision was made under (FR-005/FR-007).</summary>
    public string PolicyVersion { get; private set; } = string.Empty;

    public bool FunctionalAccepted { get; private set; }

    public bool AnalyticsAccepted { get; private set; }

    public bool MarketingAccepted { get; private set; }

    private CookieConsentRecord()
    {
        // Required by EF Core materialization.
    }

    public static CookieConsentRecord Create(
        string userId,
        string policyVersion,
        bool functionalAccepted,
        bool analyticsAccepted,
        bool marketingAccepted)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new DomainRuleViolationException("A consent record must belong to a user.");
        }

        if (string.IsNullOrWhiteSpace(policyVersion))
        {
            throw new DomainRuleViolationException("A consent record must reference a policy version.");
        }

        return new CookieConsentRecord
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            PolicyVersion = policyVersion,
            FunctionalAccepted = functionalAccepted,
            AnalyticsAccepted = analyticsAccepted,
            MarketingAccepted = marketingAccepted,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = userId,
        };
    }
}
