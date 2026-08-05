using AskLucy.Domain.Common;

namespace AskLucy.Domain.Documents;

public enum DocumentProcessingJobStatus
{
    Queued,
    InProgress,
    Completed,
    Failed,
}

/// <summary>
/// One document version's journey through the processing pipeline (FR-020, FR-027–FR-030a,
/// data-model.md). Durable, resumable job state lives here and on <see cref="DocumentProcessingStage"/>
/// — this row, not Hangfire's own internal state, is the source of truth for "what's done"
/// after a crash/restart (research.md Decisions 2, 10).
/// </summary>
public sealed class DocumentProcessingJob : BaseEntity
{
    public Guid DocumentId { get; private set; }

    public Guid DocumentVersionId { get; private set; }

    public DocumentProcessingJobStatus Status { get; private set; }

    /// <summary>The underlying Hangfire job id, for correlation/diagnostics only — never exposed to clients.</summary>
    public string? HangfireJobId { get; private set; }

    public DateTime? StartedAtUtc { get; private set; }

    public DateTime? CompletedAtUtc { get; private set; }

    /// <summary>A specific, actionable message (FR-028) — never a raw exception dump.</summary>
    public string? FailureReason { get; private set; }

    public int RetryCount { get; private set; }

    private DocumentProcessingJob()
    {
        // Required by EF Core materialization.
    }

    public static DocumentProcessingJob Create(Guid documentId, Guid documentVersionId, string actor)
    {
        return new DocumentProcessingJob
        {
            Id = Guid.CreateVersion7(),
            DocumentId = documentId,
            DocumentVersionId = documentVersionId,
            Status = DocumentProcessingJobStatus.Queued,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = actor,
        };
    }

    public void Start(string hangfireJobId, string actor)
    {
        Status = DocumentProcessingJobStatus.InProgress;
        HangfireJobId = hangfireJobId;
        StartedAtUtc ??= DateTime.UtcNow;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    public void Complete(string actor)
    {
        Status = DocumentProcessingJobStatus.Completed;
        CompletedAtUtc = DateTime.UtcNow;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    public void Fail(string failureReason, string actor)
    {
        Status = DocumentProcessingJobStatus.Failed;
        FailureReason = failureReason;
        CompletedAtUtc = DateTime.UtcNow;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    /// <summary>FR-029 — rejects a retry unless the job is currently <see cref="DocumentProcessingJobStatus.Failed"/>, preventing two concurrent processing attempts on the same document (Edge Cases).</summary>
    public void Retry(string actor)
    {
        if (Status != DocumentProcessingJobStatus.Failed)
        {
            throw new ProcessingNotInFailedStateException();
        }

        Status = DocumentProcessingJobStatus.Queued;
        FailureReason = null;
        CompletedAtUtc = null;
        RetryCount++;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }
}
