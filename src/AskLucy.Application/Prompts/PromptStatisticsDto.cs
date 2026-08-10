namespace AskLucy.Application.Prompts;

/// <summary>Manual-evaluation counts across every execution of one prompt (spec.md FR-044).</summary>
public sealed record PromptRatingBreakdownDto(int Good, int NeedsImprovement, int Failed);

/// <summary>spec.md "Prompt Statistics" API requirement, FR-062, contracts/prompts-api.md `GET /api/v1/prompts/{id}/statistics`.</summary>
public sealed record PromptStatisticsDto(
    int SuccessfulExecutionCount, DateTime? LastSuccessfulUseAtUtc, PromptRatingBreakdownDto RatingBreakdown);
