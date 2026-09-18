namespace AskLucy.Application.SiteAnalysis.Providers;

/// <summary>
/// specs/057-site-analysis-agent FR-014, data-model.md — the shared outcome vocabulary for the
/// three stub data providers below, mirroring <c>SiteBoundaryResolverTool</c>'s own
/// <c>confirmed|no_candidates|not_found|unavailable</c> shape rather than inventing a new one.
/// No release of this feature has a real implementation of any of these three interfaces
/// (plan.md Complexity Tracking) — every implementation available today returns
/// <see cref="DataResolutionOutcomeType.Unavailable"/>, and a specialist consuming one renders
/// "no data source available" rather than presenting an invented figure as measured (FR-014).
/// </summary>
public enum DataResolutionOutcomeType
{
    Confirmed,
    NoCandidates,
    Ambiguous,
    Unavailable,
}

public sealed record DataResolutionOutcome<T>(DataResolutionOutcomeType Type, T? Value, string? Reason)
{
    public static DataResolutionOutcome<T> Confirmed(T value) => new(DataResolutionOutcomeType.Confirmed, value, null);

    public static DataResolutionOutcome<T> Unavailable(string reason) => new(DataResolutionOutcomeType.Unavailable, default, reason);
}
