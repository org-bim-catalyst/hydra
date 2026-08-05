namespace AskLucy.Domain.Documents;

/// <summary>
/// Thrown by <see cref="DocumentProcessingJob.Retry"/> when the current job isn't
/// <see cref="DocumentProcessingJobStatus.Failed"/> (FR-029) — mapped to <c>409 Conflict</c>
/// with <c>reason: "NotInFailedState"</c> (contracts/document-processing-api.md), distinct from
/// <see cref="AskLucy.Domain.Common.DomainRuleViolationException"/>'s generic 400 mapping because
/// this is a conflict with current state, not an invalid request.
/// </summary>
public sealed class ProcessingNotInFailedStateException() : Exception("Only a failed processing job can be retried.");
