namespace AskLucy.Infrastructure.Email;

/// <summary>
/// Bound from configuration ("Smtp" section). Replaces SendGrid per the 2026-07-28 decision
/// to move off SendGrid onto the organization's own mail host.
/// </summary>
public sealed class SmtpOptions
{
    public const string SectionName = "Smtp";

    public string Host { get; init; } = string.Empty;

    public int Port { get; init; } = 587;

    /// <summary>Implicit TLS from the first byte of the connection (usually port 465).</summary>
    public bool UseSsl { get; init; }

    /// <summary>Upgrades a plaintext connection to TLS via STARTTLS (usually port 587). The
    /// common case, and mutually exclusive with <see cref="UseSsl"/> — if both are false,
    /// the connection is unencrypted.</summary>
    public bool UseStartTls { get; init; } = true;

    /// <summary>Never committed — set via user-secrets/environment/appsettings.Production.json.</summary>
    public string Username { get; init; } = string.Empty;

    /// <summary>Never committed — set via user-secrets/environment/appsettings.Production.json.</summary>
    public string Password { get; init; } = string.Empty;

    public string FromName { get; init; } = "Ask Lucy";

    /// <summary>Used for every current email send (registration/email-change confirmation, password reset, 2FA) — all transactional.</summary>
    public string FromTransactional { get; init; } = "noreply@bimcatalyst.com";

    /// <summary>Configured for future use; no current call site sends a support-category email.</summary>
    public string FromSupport { get; init; } = "support@bimcatalyst.com";

    /// <summary>Configured for future use; no current call site sends a privacy-category email.</summary>
    public string FromPrivacy { get; init; } = "privacy@bimcatalyst.com";
}
