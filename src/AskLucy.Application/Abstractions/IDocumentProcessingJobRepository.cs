using AskLucy.Domain.Documents;

namespace AskLucy.Application.Abstractions;

/// <summary>US6 dashboard counts (FR-045/FR-045a) — each based on only the current (latest) processing job per document, never a historical count across every job a document ever had.</summary>
public sealed record DocumentDashboardCounts(int QueueDepth, int InProgressCount, int CompletedTodayCount, int FailedCount);

/// <summary>One entry in the retry-queue view (US6 AC2, contracts/document-processing-api.md).</summary>
public sealed record DocumentRetryQueueEntry(Guid DocumentId, string FileName, string FailureReason);

/// <summary>Repository for <see cref="DocumentProcessingJob"/> (constitution §3 Repository rules).</summary>
public interface IDocumentProcessingJobRepository
{
    Task<DocumentProcessingJob?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>The most recent processing job for a document — a document's displayed <see cref="Document.ProcessingStatus"/> mirrors this job's status (data-model.md).</summary>
    Task<DocumentProcessingJob?> GetCurrentForDocumentAsync(Guid documentId, CancellationToken cancellationToken = default);

    void Add(DocumentProcessingJob job);

    Task<IReadOnlyList<DocumentProcessingStage>> GetStagesAsync(Guid documentProcessingJobId, CancellationToken cancellationToken = default);

    void AddStage(DocumentProcessingStage stage);

    void AddLog(DocumentProcessingLog log);

    /// <summary>Newest-first — the visible processing history (FR-013).</summary>
    Task<IReadOnlyList<DocumentProcessingLog>> GetLogsByDocumentIdAsync(Guid documentId, CancellationToken cancellationToken = default);

    /// <summary>US6 dashboard counts (FR-045/FR-045a) scoped to <paramref name="ownerId"/>, or organization-wide when null.</summary>
    Task<DocumentDashboardCounts> GetDashboardCountsAsync(string? ownerId, DateTime todayStartUtc, CancellationToken cancellationToken = default);

    /// <summary>The documents whose current job is `Failed` (US6 AC2), scoped to <paramref name="ownerId"/>, or organization-wide when null.</summary>
    Task<IReadOnlyList<DocumentRetryQueueEntry>> GetRetryQueueAsync(string? ownerId, CancellationToken cancellationToken = default);
}
