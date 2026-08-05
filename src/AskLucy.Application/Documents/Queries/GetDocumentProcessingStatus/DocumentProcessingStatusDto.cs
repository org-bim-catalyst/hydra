using AskLucy.Domain.Documents;

namespace AskLucy.Application.Documents.Queries.GetDocumentProcessingStatus;

/// <summary>contracts/document-processing-api.md's per-document status shape (FR-027, US2 AC1).</summary>
public sealed record DocumentProcessingStatusDto(
    Guid DocumentId,
    DocumentProcessingStatus ProcessingStatus,
    DocumentProcessingStageType? CurrentStage,
    IReadOnlyList<DocumentProcessingStageDto> Stages,
    string? FailureReason)
{
    public static DocumentProcessingStatusDto FromEntities(
        Document document, DocumentProcessingJob job, IReadOnlyList<DocumentProcessingStage> stages) => new(
        document.Id,
        document.ProcessingStatus,
        stages.FirstOrDefault(s => s.Status == DocumentProcessingStageStatus.InProgress)?.StageType,
        stages.OrderBy(s => (int)s.StageType).Select(DocumentProcessingStageDto.FromEntity).ToList(),
        job.FailureReason);
}

public sealed record DocumentProcessingStageDto(
    DocumentProcessingStageType StageType,
    DocumentProcessingStageStatus Status,
    DateTime? StartedAtUtc,
    DateTime? CompletedAtUtc)
{
    public static DocumentProcessingStageDto FromEntity(DocumentProcessingStage stage) => new(
        stage.StageType, stage.Status, stage.StartedAtUtc, stage.CompletedAtUtc);
}
