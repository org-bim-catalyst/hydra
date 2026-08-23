using AskLucy.Application.SiteAnalysis;

namespace AskLucy.Application.Abstractions;

/// <summary>A candidate Project found by <see cref="ITheDigitalCoreClient.FindProjectAsync"/> (contracts/thedigitalcore-integration-api.md).</summary>
public sealed record TheDigitalCoreProjectCandidate(string ProjectId, string Name, decimal? Latitude, decimal? Longitude, string? CompanyId);

/// <summary>
/// Ask Lucy's side of the consumer-driven contract with TheDigitalCore
/// (contracts/thedigitalcore-integration-api.md, ADR-0009). Every call is made under a single,
/// dedicated Ask Lucy service-account credential (Clarifications Q2, FR-026/FR-027) — never a
/// per-user TheDigitalCore credential.
/// </summary>
public interface ITheDigitalCoreClient
{
    /// <summary>Name-first, geolocation-secondary search (research.md Decision 8, Clarifications Q3). Empty list = no match found.</summary>
    Task<IReadOnlyList<TheDigitalCoreProjectCandidate>> FindProjectAsync(string siteName, decimal? latitude, decimal? longitude, CancellationToken cancellationToken = default);

    /// <summary>Creates a new Project. Callers MUST only invoke this after explicit user confirmation (FR-001e/FR-001f).</summary>
    Task<string> CreateProjectAsync(string siteName, decimal? latitude, decimal? longitude, CancellationToken cancellationToken = default);

    /// <summary>Relays a completed Category Score Result for persistence against the given Project (FR-026, contracts/site-analysis-category-result.md).</summary>
    Task RelayCategoryScoreResultAsync(string theDigitalCoreProjectId, CategoryScoreResultDto result, CancellationToken cancellationToken = default);
}
