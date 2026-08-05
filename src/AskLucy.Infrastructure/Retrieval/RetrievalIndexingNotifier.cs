using AskLucy.Application.Abstractions;
using Microsoft.AspNetCore.SignalR;

namespace AskLucy.Infrastructure.Retrieval;

/// <summary>
/// <see cref="IRetrievalIndexingNotifier"/> implementation — pushes over
/// <see cref="RetrievalIndexingHub"/> (FR-014, FR-039, research.md Decision 7), mirroring
/// <c>ProcessingNotifier</c> (specs/015).
/// </summary>
public sealed class RetrievalIndexingNotifier(IHubContext<RetrievalIndexingHub> hubContext) : IRetrievalIndexingNotifier
{
    public Task NotifyIndexStatusChangedAsync(string ownerUserId, Guid knowledgeBaseId, string indexStatus, CancellationToken cancellationToken = default) =>
        hubContext.Clients.Group(RetrievalIndexingHub.UserGroup(ownerUserId))
            .SendAsync("knowledgeBaseIndexStatusChanged", new { knowledgeBaseId, indexStatus }, cancellationToken);

    public Task NotifyStageChangedAsync(string ownerUserId, Guid knowledgeBaseId, Guid jobId, string stage, string status, CancellationToken cancellationToken = default) =>
        hubContext.Clients.Group(RetrievalIndexingHub.UserGroup(ownerUserId))
            .SendAsync("indexingStageChanged", new { knowledgeBaseId, jobId, stage, status }, cancellationToken);

    public Task NotifyJobFailedAsync(string ownerUserId, Guid knowledgeBaseId, Guid jobId, string failureReason, CancellationToken cancellationToken = default) =>
        hubContext.Clients.Group(RetrievalIndexingHub.UserGroup(ownerUserId))
            .SendAsync("indexingJobFailed", new { knowledgeBaseId, jobId, failureReason }, cancellationToken);
}
