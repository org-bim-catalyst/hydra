using AskLucy.Application.Abstractions;
using AskLucy.Domain.Documents;

namespace AskLucy.Application.Documents.Processing.Stages;

/// <summary>
/// Re-confirms the stored file is still readable/intact before heavier stages run (FR-010's
/// upload-time validation already ran once; this is a defensive re-check, not a duplicate
/// content-type decision). Corrupted-file/password-protected-content failures surface here or
/// in the extraction stages that actually try to read the content (Edge Cases).
/// </summary>
public sealed class ValidationStageHandler(
    IDocumentRepository documentRepository,
    IFileStorage fileStorage,
    IDocumentFileValidator fileValidator) : IProcessingStageHandler
{
    public DocumentProcessingStageType StageType => DocumentProcessingStageType.Validation;

    public async Task<ProcessingStageOutcome> ExecuteAsync(Guid documentId, Guid documentVersionId, CancellationToken cancellationToken = default)
    {
        var version = await documentRepository.GetVersionByIdAsync(documentVersionId, cancellationToken)
            ?? throw new KeyNotFoundException("Document version not found.");

        await using var content = await fileStorage.OpenReadAsync(version.StoredFileName, cancellationToken);
        var validation = await fileValidator.ValidateAsync(content, version.OriginalFileName, cancellationToken);

        if (!validation.IsValid)
        {
            throw new InvalidOperationException(validation.FailureReason ?? "The stored file could not be re-validated.");
        }

        return ProcessingStageOutcome.Completed;
    }
}
