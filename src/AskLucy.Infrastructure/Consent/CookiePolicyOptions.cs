using System.ComponentModel.DataAnnotations;

namespace AskLucy.Infrastructure.Consent;

/// <summary>Bound from configuration (constitution &#167;4), validated at startup (specs/004-cookie-consent-privacy).</summary>
public sealed class CookiePolicyOptions
{
    public const string SectionName = "CookiePolicy";

    [Required]
    public string CurrentVersion { get; set; } = string.Empty;

    public DateTime EffectiveAtUtc { get; set; }
}
