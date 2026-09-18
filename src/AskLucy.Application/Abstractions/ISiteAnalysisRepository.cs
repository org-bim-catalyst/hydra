using SiteAnalysisAggregate = AskLucy.Domain.SiteAnalysis.SiteAnalysis;

namespace AskLucy.Application.Abstractions;

/// <summary>Aggregate-oriented repository for <see cref="SiteAnalysisAggregate"/> (constitution &#167;3 Repository rules) — no <c>IQueryable</c> escape hatch.</summary>
public interface ISiteAnalysisRepository
{
    void Add(SiteAnalysisAggregate analysis);

    Task<SiteAnalysisAggregate?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Ownership-scoped read (FR-019) — the retrieval endpoint's only path to an analysis; an analysis belonging to another user is indistinguishable from one that does not exist.</summary>
    Task<SiteAnalysisAggregate?> GetByIdForUserAsync(Guid id, string userId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SiteAnalysisAggregate>> ListByChatAsync(string userId, Guid userChatId, CancellationToken cancellationToken = default);

    /// <summary>specs/057-site-analysis-agent tasks.md T010 — the in-flight analysis for this chat and site, or null. Used by <c>RequestSiteAnalysisCapability</c> to reuse a running analysis rather than dispatching a duplicate.</summary>
    Task<SiteAnalysisAggregate?> FindRunningForSiteAsync(string userId, Guid userChatId, string siteName, CancellationToken cancellationToken = default);

    /// <summary>
    /// A targeted, non-tracked update — bypasses optimistic concurrency entirely, mirroring
    /// <c>IUserChatRepository.MarkMemoryAnalyzedAsync</c>'s exact reasoning: this is monotonic
    /// diagnostic bookkeeping (research.md D12's "diagnostic link to the fan-out execution"), not
    /// something a concurrent specialist report could meaningfully conflict with — last writer
    /// wins is correct here, and there is nothing for a concurrent edit to corrupt. Using the
    /// tracked aggregate's own <c>RowVersion</c> for this would race the just-dispatched
    /// workflow's own (fast) specialist report, which is a real, observed failure mode elsewhere
    /// in this codebase for the identical "diagnostic write racing a background job" shape.
    /// </summary>
    Task RecordWorkflowExecutionIdAsync(Guid siteAnalysisId, Guid workflowExecutionId, CancellationToken cancellationToken = default);
}
