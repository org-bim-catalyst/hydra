namespace AskLucy.Application.SiteAnalysis;

/// <summary>A citation identifying the supporting evidence behind one finding (contracts/site-analysis-category-result.md).</summary>
public sealed record CategoryFindingCitationDto(string DocumentTitle, string Passage, string SourceRef);

/// <summary>One finding produced by a Category Analysis Run, always citation-backed (FR-017, SC-003).</summary>
public sealed record CategoryFindingDto(string Title, string Type, string TriggeringMetric, CategoryFindingCitationDto Citation);

/// <summary>An explicit record that a required data input could not be obtained (FR-015/FR-016).</summary>
public sealed record CategoryDataGapDto(string Field, string Reason);

/// <summary>
/// The shape held on <c>AgentExecution.FinalOutputJson</c> for a Recreation/Social Category
/// Analysis Run (research.md Decision 6, contracts/site-analysis-category-result.md) — no
/// dedicated database table exists for this; it is also the Floating Panel payload shape
/// (research.md Decision 10).
/// </summary>
public sealed record CategoryScoreResultDto(
    string Category,
    string SiteName,
    decimal Score,
    IReadOnlyList<CategoryFindingDto> Findings,
    IReadOnlyList<CategoryDataGapDto> DataGaps,
    bool RequiresReview,
    string? ReviewReason,
    Guid AgentExecutionId);
