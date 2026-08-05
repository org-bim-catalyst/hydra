namespace AskLucy.Application.Abstractions;

/// <summary>
/// Pushes indexing status/progress to connected clients (research.md Decision 7, mirrors
/// <c>IProcessingNotifier</c> from specs/015). Implemented in <c>Infrastructure</c> over the
/// <c>RetrievalIndexingHub</c> SignalR hub; every notification targets only the knowledge base
/// owner's own connection group, never a broadcast.
/// </summary>
public interface IRetrievalIndexingNotifier
{
    Task NotifyIndexStatusChangedAsync(string ownerUserId, Guid knowledgeBaseId, string indexStatus, CancellationToken cancellationToken = default);

    Task NotifyStageChangedAsync(string ownerUserId, Guid knowledgeBaseId, Guid jobId, string stage, string status, CancellationToken cancellationToken = default);

    Task NotifyJobFailedAsync(string ownerUserId, Guid knowledgeBaseId, Guid jobId, string failureReason, CancellationToken cancellationToken = default);
}
