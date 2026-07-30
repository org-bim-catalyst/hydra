namespace AskLucy.Web.Contracts;

/// <summary>
/// Full-replace save of the caller's cookie preferences (contracts/cookie-consent-api.md).
/// Fields are nullable so an omitted field is a real, validatable "missing" state, distinct
/// from an explicit <c>false</c> (a non-nullable <c>bool</c> can't tell the two apart).
/// Essential is never accepted here — it is not a client-controlled value (spec.md FR-004).
/// </summary>
public sealed record SaveCookieConsentRequest(bool? Functional, bool? Analytics, bool? Marketing);

public sealed record CookieConsentResponse(
    bool HasConsented,
    bool RequiresReconsent,
    string? PolicyVersion,
    string CurrentPolicyVersion,
    bool Essential,
    bool Functional,
    bool Analytics,
    bool Marketing,
    DateTime? LastUpdatedAtUtc);

public sealed record CookiePolicyResponse(string Version, DateTime EffectiveAtUtc);
