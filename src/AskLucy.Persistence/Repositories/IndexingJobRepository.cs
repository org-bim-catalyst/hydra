using AskLucy.Application.Abstractions;
using AskLucy.Domain.Retrieval;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Repositories;

public sealed class IndexingJobRepository(AskLucyDbContext dbContext) : IIndexingJobRepository
{
    public Task<IndexingJob?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.IndexingJobs.FirstOrDefaultAsync(j => j.Id == id, cancellationToken);

    public Task<IndexingJob?> GetCurrentForKnowledgeBaseAsync(Guid knowledgeBaseId, CancellationToken cancellationToken = default) =>
        dbContext.IndexingJobs
            .Where(j => j.KnowledgeBaseId == knowledgeBaseId && j.KnowledgeBaseDocumentId == null)
            .OrderByDescending(j => j.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<bool> HasJobInProgressAsync(Guid knowledgeBaseId, CancellationToken cancellationToken = default) =>
        dbContext.IndexingJobs.AnyAsync(
            j => j.KnowledgeBaseId == knowledgeBaseId
                 && j.KnowledgeBaseDocumentId == null
                 && (j.Status == IndexingJobStatus.Queued || j.Status == IndexingJobStatus.InProgress),
            cancellationToken);

    public void Add(IndexingJob job) => dbContext.IndexingJobs.Add(job);

    public async Task<IReadOnlyList<IndexingLog>> GetLogsAsync(Guid indexingJobId, CancellationToken cancellationToken = default) =>
        await dbContext.IndexingLogs
            .Where(l => l.IndexingJobId == indexingJobId)
            .OrderByDescending(l => l.OccurredAtUtc)
            .ToListAsync(cancellationToken);

    public void AddLog(IndexingLog log) => dbContext.IndexingLogs.Add(log);
}
