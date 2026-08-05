using AskLucy.Domain.Common;

namespace AskLucy.Domain.Documents;

/// <summary>Matches spec.md's Processing Pipeline order. Virus Scan is explicitly future work (spec.md Assumptions) — not modeled as a stage yet.</summary>
public enum DocumentProcessingStageType
{
    Validation,
    Ocr,
    TextExtraction,
    MetadataExtraction,
    Classification,
    LanguageDetection,
    PreviewGeneration,
}

public enum DocumentProcessingStageStatus
{
    Pending,
    InProgress,
    Completed,
    Failed,

    /// <summary>E.g. OCR being unnecessary because the document already has a text layer.</summary>
    Skipped,
}

/// <summary>
/// One step within a <see cref="DocumentProcessingJob"/> (FR-027, FR-029, FR-030a, data-model.md).
/// On job resume after a restart (research.md Decision 10), any stage already
/// <see cref="DocumentProcessingStageStatus.Completed"/> is never re-executed — the pipeline
/// orchestrator resumes from the first <see cref="DocumentProcessingStageStatus.Pending"/> stage.
/// </summary>
public sealed class DocumentProcessingStage : BaseEntity
{
    public Guid DocumentProcessingJobId { get; private set; }

    public DocumentProcessingStageType StageType { get; private set; }

    public DocumentProcessingStageStatus Status { get; private set; }

    public DateTime? StartedAtUtc { get; private set; }

    public DateTime? CompletedAtUtc { get; private set; }

    public string? FailureReason { get; private set; }

    private DocumentProcessingStage()
    {
        // Required by EF Core materialization.
    }

    public static DocumentProcessingStage Create(Guid documentProcessingJobId, DocumentProcessingStageType stageType, string actor)
    {
        return new DocumentProcessingStage
        {
            Id = Guid.CreateVersion7(),
            DocumentProcessingJobId = documentProcessingJobId,
            StageType = stageType,
            Status = DocumentProcessingStageStatus.Pending,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = actor,
        };
    }

    public void Start(string actor)
    {
        Status = DocumentProcessingStageStatus.InProgress;
        StartedAtUtc = DateTime.UtcNow;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    public void Complete(string actor)
    {
        Status = DocumentProcessingStageStatus.Completed;
        CompletedAtUtc = DateTime.UtcNow;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    public void Fail(string failureReason, string actor)
    {
        Status = DocumentProcessingStageStatus.Failed;
        FailureReason = failureReason;
        CompletedAtUtc = DateTime.UtcNow;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }

    public void Skip(string actor)
    {
        Status = DocumentProcessingStageStatus.Skipped;
        CompletedAtUtc = DateTime.UtcNow;
        ModifiedAtUtc = DateTime.UtcNow;
        ModifiedBy = actor;
    }
}
