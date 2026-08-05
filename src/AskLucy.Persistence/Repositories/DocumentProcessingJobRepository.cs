using AskLucy.Application.Abstractions;
using AskLucy.Domain.Documents;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Repositories;

public sealed class DocumentProcessingJobRepository(AskLucyDbContext dbContext) : IDocumentProcessingJobRepository
{
    public Task<DocumentProcessingJob?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.DocumentProcessingJobs.FirstOrDefaultAsync(j => j.Id == id, cancellationToken);

    public Task<DocumentProcessingJob?> GetCurrentForDocumentAsync(Guid documentId, CancellationToken cancellationToken = default) =>
        dbContext.DocumentProcessingJobs
            .Where(j => j.DocumentId == documentId)
            .OrderByDescending(j => j.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

    public void Add(DocumentProcessingJob job) => dbContext.DocumentProcessingJobs.Add(job);

    public async Task<IReadOnlyList<DocumentProcessingStage>> GetStagesAsync(Guid documentProcessingJobId, CancellationToken cancellationToken = default) =>
        await dbContext.DocumentProcessingStages
            .Where(s => s.DocumentProcessingJobId == documentProcessingJobId)
            .ToListAsync(cancellationToken);

    public void AddStage(DocumentProcessingStage stage) => dbContext.DocumentProcessingStages.Add(stage);

    public void AddLog(DocumentProcessingLog log) => dbContext.DocumentProcessingLogs.Add(log);

    public async Task<IReadOnlyList<DocumentProcessingLog>> GetLogsByDocumentIdAsync(Guid documentId, CancellationToken cancellationToken = default) =>
        await dbContext.DocumentProcessingLogs
            .Where(l => l.DocumentId == documentId)
            .OrderByDescending(l => l.OccurredAtUtc)
            .ToListAsync(cancellationToken);

    /// <summary>Only the latest job per document — a document that failed once and later succeeded after a retry must never be double-counted as both Failed and Completed (US6).</summary>
    private IQueryable<DocumentProcessingJob> GetCurrentJobsQuery(string? ownerId)
    {
        var jobsQuery = ownerId is null
            ? dbContext.DocumentProcessingJobs
            : dbContext.DocumentProcessingJobs.Join(
                dbContext.Documents.Where(d => d.OwnerId == ownerId), j => j.DocumentId, d => d.Id, (j, _) => j);

        return jobsQuery
            .GroupBy(j => j.DocumentId)
            .Select(g => g.OrderByDescending(j => j.CreatedAtUtc).First());
    }

    /// <summary>
    /// Polled every 5s per session (research.md Decision 7's reconciliation fallback) — the
    /// four counts below deliberately share one materialization of the "latest job per
    /// document" grouped subquery rather than four separate <c>CountAsync</c> calls each
    /// re-running that GroupBy/window aggregation against the full jobs table.
    /// </summary>
    public async Task<DocumentDashboardCounts> GetDashboardCountsAsync(string? ownerId, DateTime todayStartUtc, CancellationToken cancellationToken = default)
    {
        var currentJobs = await GetCurrentJobsQuery(ownerId)
            .Select(j => new { j.Status, j.CompletedAtUtc })
            .ToListAsync(cancellationToken);

        var queueDepth = currentJobs.Count(j => j.Status == DocumentProcessingJobStatus.Queued);
        var inProgressCount = currentJobs.Count(j => j.Status == DocumentProcessingJobStatus.InProgress);
        var completedTodayCount = currentJobs.Count(
            j => j.Status == DocumentProcessingJobStatus.Completed && j.CompletedAtUtc is not null && j.CompletedAtUtc >= todayStartUtc);
        var failedCount = currentJobs.Count(j => j.Status == DocumentProcessingJobStatus.Failed);

        return new DocumentDashboardCounts(queueDepth, inProgressCount, completedTodayCount, failedCount);
    }

    public async Task<IReadOnlyList<DocumentRetryQueueEntry>> GetRetryQueueAsync(string? ownerId, CancellationToken cancellationToken = default)
    {
        var failedCurrentJobs = GetCurrentJobsQuery(ownerId).Where(j => j.Status == DocumentProcessingJobStatus.Failed);

        return await (
            from job in failedCurrentJobs
            join document in dbContext.Documents on job.DocumentId equals document.Id
            select new DocumentRetryQueueEntry(document.Id, document.FileName, job.FailureReason ?? "Unknown failure."))
            .ToListAsync(cancellationToken);
    }
}
