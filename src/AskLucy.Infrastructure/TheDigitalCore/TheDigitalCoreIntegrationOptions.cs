namespace AskLucy.Infrastructure.TheDigitalCore;

/// <summary>
/// Bound from configuration/environment (constitution &#167;4/&#167;8). TheDigitalCore has no
/// long-lived static credential to hand out (Clarifications Q2, ADR-0009 update) — it issues a
/// short-lived JWT via a client-id/client-secret exchange at
/// <c>POST /api/service-auth/token</c>, obtained and cached in memory by
/// <see cref="ITheDigitalCoreAuthService"/>. Neither the secret nor any minted token is ever
/// committed to <c>appsettings.json</c> or exposed to a browser context (FR-027) &#8212; sourced via
/// user-secrets/environment/secret manager only.
/// </summary>
public sealed class TheDigitalCoreIntegrationOptions
{
    public const string SectionName = "TheDigitalCoreIntegration";

    public required string BaseUrl { get; init; }

    public required string ServiceAccountClientId { get; init; }

    public required string ServiceAccountClientSecret { get; init; }

    /// <summary>TheDigitalCore's <c>Project.CompanyId</c>/<c>ProjectTypeId</c> are required foreign
    /// keys with no Ask Lucy equivalent (discovered when reconciling against the real API) &#8212;
    /// these identify the Company/ProjectType a bootstrap-created Project is filed under.</summary>
    public required int DefaultCompanyId { get; init; }

    public required int DefaultProjectTypeId { get; init; }

    public int TimeoutSeconds { get; init; } = 30;

    public int MaxRetries { get; init; } = 3;
}
