using AskLucy.Domain.Common;

namespace AskLucy.Domain.Retrieval;

/// <summary>What triggered/scopes an indexing job (spec.md FR-010a, FR-011, FR-012).</summary>
public enum IndexingJobType
{
    InitialIndex,
    FullReindex,
    IncrementalReindex,
    VersionReindex,
    SingleDocumentIndex,
}

public enum IndexingJobStatus
{
    Queued,
    InProgress,
    Completed,
    Failed,
}

/// <summary>
/// A unit of background work indexing a knowledge base, document, or document version (spec.md
/// FR-010–FR-013, FR-038, data-model.md). Mirrors the shape of <c>DocumentProcessingJob</c>
/// (specs/015).
/// </summary>
public sealed class IndexingJob : BaseEntity
{
    public Guid KnowledgeBaseId { get; private set; }

    public Guid? KnowledgeBaseDocumentId { get; private set; }

    public IndexingJobType JobType { get; private set; }

    public IndexingJobStatus Status { get; private set; }

    public int RetryCount { get; private set; }

    public int MaxRetries { get; private set; }

    public string? HangfireJobId { get; private set; }

    public DateTime? StartedAtUtc { get; private set; }

    public DateTime? CompletedAtUtc { get; private set; }

    public string? FailureReason { get; private set; }

    public bool HasExhaustedRetries => RetryCount >= MaxRetries;

    private IndexingJob()
    {
        // Required by EF Core materialization.
    }

    public static IndexingJob Create(Guid knowledgeBaseId, Guid? knowledgeBaseDocumentId, IndexingJobType jobType, int maxRetries, string actor)
    {
        if (maxRetries < 0)
        {
            throw new DomainRuleViolationException("Max retries cannot be negative.");
        }

        return new IndexingJob
        {
            Id = Guid.CreateVersion7(),
            KnowledgeBaseId = knowledgeBaseId,
            KnowledgeBaseDocumentId = knowledgeBaseDocumentId,
            JobType = jobType,
            Status = IndexingJobStatus.Queued,
            RetryCount = 0,
            MaxRetries = maxRetries,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = actor,
        };
    }

    public void AssignHangfireJobId(string hangfireJobId, string actor)
    {
        if (string.IsNullOrWhiteSpace(hangfireJobId))
        {
            throw new DomainRuleViolationException("A Hangfire job id is required.");
        }

        HangfireJobId = hangfireJobId;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    public void Start(string actor)
    {
        Status = IndexingJobStatus.InProgress;
        StartedAtUtc = DateTime.UtcNow;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    public void Complete(string actor)
    {
        Status = IndexingJobStatus.Completed;
        CompletedAtUtc = DateTime.UtcNow;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    /// <summary>FR-013 — records a specific, actionable failure reason; bumps the retry count toward the bounded-retry policy (FR-040).</summary>
    public void Fail(string failureReason, string actor)
    {
        if (string.IsNullOrWhiteSpace(failureReason))
        {
            throw new DomainRuleViolationException("A failure reason is required.");
        }

        Status = IndexingJobStatus.Failed;
        FailureReason = failureReason;
        RetryCount++;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    /// <summary>FR-013/FR-040 — re-queues after a failure for another attempt.</summary>
    public void Requeue(string actor)
    {
        if (Status != IndexingJobStatus.Failed)
        {
            throw new DomainRuleViolationException("Only a failed indexing job can be requeued.");
        }

        Status = IndexingJobStatus.Queued;
        FailureReason = null;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }
}
