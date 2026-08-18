using AskLucy.Application.Prompts;
using AskLucy.Domain.Prompts;

namespace AskLucy.Application.Abstractions;

/// <summary>Repository for <see cref="PromptExecution"/> and its 1:1 <see cref="PromptExecutionResult"/>/<see cref="PromptRating"/> (spec.md FR-040–FR-046, FR-080).</summary>
public interface IPromptExecutionRepository
{
    void Add(PromptExecution execution);

    void AddResult(PromptExecutionResult result);

    Task<PromptExecution?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<PromptExecutionResult?> GetResultByExecutionIdAsync(Guid promptExecutionId, CancellationToken cancellationToken = default);

    Task<PromptRating?> GetRatingByExecutionIdAsync(Guid promptExecutionId, CancellationToken cancellationToken = default);

    void AddRating(PromptRating rating);

    /// <summary>Cursor-paginated execution history for one prompt, newest first (FR-042).</summary>
    Task<(IReadOnlyList<PromptExecution> Items, string? NextCursor)> ListForPromptAsync(
        Guid promptId, string? cursor, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>Batch lookup for side-by-side comparison (FR-045).</summary>
    Task<IReadOnlyList<PromptExecution>> ListByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default);

    /// <summary>Manual-evaluation counts across every execution of one prompt (FR-044, FR-062, `GetPromptStatisticsQuery`).</summary>
    Task<PromptRatingBreakdownDto> GetRatingBreakdownByPromptIdAsync(Guid promptId, CancellationToken cancellationToken = default);
}
