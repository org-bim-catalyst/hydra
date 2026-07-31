namespace AskLucy.Application.Abstractions;

/// <summary>
/// The currently published cookie/privacy policy version (specs/004-cookie-consent-privacy,
/// research.md Topic 3). Backed by configuration, not a database table — a version bump is a
/// deliberate, legally-reviewed deploy-time change, not a runtime admin workflow.
/// </summary>
public interface ICookiePolicyProvider
{
    (string Version, DateTime EffectiveAtUtc) GetCurrentPolicy();
}
