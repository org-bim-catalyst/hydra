namespace AskLucy.Application.Consent;

/// <summary>The publicly-visible cookie/privacy policy version, backing the Privacy Page (specs/004-cookie-consent-privacy).</summary>
public sealed record CookiePolicyDto(string Version, DateTime EffectiveAtUtc);
