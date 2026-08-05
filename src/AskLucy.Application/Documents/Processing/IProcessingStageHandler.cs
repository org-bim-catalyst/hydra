using AskLucy.Domain.Documents;

namespace AskLucy.Application.Documents.Processing;

/// <summary>
/// One implementation per <see cref="DocumentProcessingStageType"/> (Strategy pattern,
/// research.md Decisions 2/10), resolved by <see cref="IDocumentProcessingPipeline"/> — never
/// invoked directly by a command handler. Implementations are added per-stage in US2
/// (specs/015-document-intelligence-pipeline tasks.md T072); this interface is the shared
/// contract every one of them satisfies.
/// </summary>
/// <summary>Whether a stage did real work or determined none was needed for this document (e.g. OCR on a PDF that already has a text layer) — the orchestrator persists this distinction on <see cref="DocumentProcessingStage.Status"/> (<c>Completed</c> vs <c>Skipped</c>).</summary>
public enum ProcessingStageOutcome
{
    Completed,
    Skipped,
}

public interface IProcessingStageHandler
{
    DocumentProcessingStageType StageType { get; }

    /// <summary>
    /// Executes this stage for the given document/version. A thrown exception signals failure —
    /// the orchestrator catches it, records a specific <see cref="DocumentProcessingStage.FailureReason"/>
    /// (FR-028), and stops the chain rather than letting it bubble as an unhandled exception.
    /// </summary>
    Task<ProcessingStageOutcome> ExecuteAsync(Guid documentId, Guid documentVersionId, CancellationToken cancellationToken = default);
}
