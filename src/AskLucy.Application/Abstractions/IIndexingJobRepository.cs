using AskLucy.Domain.Retrieval;

namespace AskLucy.Application.Abstractions;

/// <summary>Repository for <see cref="IndexingJob"/> (constitution §3 Repository rules).</summary>
public interface IIndexingJobRepository
{
    Task<IndexingJob?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>The most recent job for a knowledge base (whole-KB scoped jobs only — <see cref="IndexingJob.KnowledgeBaseDocumentId"/> null).</summary>
    Task<IndexingJob?> GetCurrentForKnowledgeBaseAsync(Guid knowledgeBaseId, CancellationToken cancellationToken = default);

    /// <summary>§5 Concurrency — true if a knowledge-base-scoped job is already <c>Queued</c>/<c>InProgress</c> (Edge Case: two concurrent reindex triggers).</summary>
    Task<bool> HasJobInProgressAsync(Guid knowledgeBaseId, CancellationToken cancellationToken = default);

    void Add(IndexingJob job);

    Task<IReadOnlyList<IndexingLog>> GetLogsAsync(Guid indexingJobId, CancellationToken cancellationToken = default);

    void AddLog(IndexingLog log);
}
