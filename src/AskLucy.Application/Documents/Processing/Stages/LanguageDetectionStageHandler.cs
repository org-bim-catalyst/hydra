using AskLucy.Domain.Documents;

namespace AskLucy.Application.Documents.Processing.Stages;

/// <summary>
/// FR-024 — language detection. Its actual work is performed together with classification in
/// <see cref="ClassificationStageHandler"/> (one AI call covers both, research.md Decision 4).
/// This stage is a deliberate, documented no-op: the orchestrator still models it as its own
/// pipeline stage (matching spec.md's pipeline order and FR-012's lifecycle sub-stages), but it
/// never re-invokes the classifier — doing so would double the AI cost per document for no
/// benefit, since <see cref="ClassificationStageHandler"/> always runs first in the fixed stage
/// order and has already persisted the <see cref="DocumentLanguage"/> rows by the time this runs.
/// </summary>
public sealed class LanguageDetectionStageHandler : IProcessingStageHandler
{
    public DocumentProcessingStageType StageType => DocumentProcessingStageType.LanguageDetection;

    public Task<ProcessingStageOutcome> ExecuteAsync(Guid documentId, Guid documentVersionId, CancellationToken cancellationToken = default) =>
        Task.FromResult(ProcessingStageOutcome.Skipped);
}
