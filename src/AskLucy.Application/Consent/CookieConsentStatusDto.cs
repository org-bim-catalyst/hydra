namespace AskLucy.Application.Consent;

/// <summary>
/// The caller's current cookie-consent status (specs/004-cookie-consent-privacy,
/// data-model.md). <see cref="Essential"/> is always <c>true</c> — a constant, never a
/// stored value (research.md Topic 4).
/// </summary>
public sealed record CookieConsentStatusDto(
    bool HasConsented,
    bool RequiresReconsent,
    string? PolicyVersion,
    string CurrentPolicyVersion,
    bool Essential,
    bool Functional,
    bool Analytics,
    bool Marketing,
    DateTime? LastUpdatedAtUtc);
