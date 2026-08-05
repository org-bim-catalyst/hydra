using AskLucy.Domain.Chats;
using AskLucy.Domain.KnowledgeBases;
using AskLucy.Domain.Retrieval;

namespace AskLucy.Application.Retrieval.Authorization;

/// <summary>
/// Ownership checks specific to Retrieval entities that don't map 1:1 onto an existing aggregate
/// (constitution §3, Dependency Rule — no ASP.NET Core authorization types referenced here).
/// Retrieval commands operating directly on a <c>KnowledgeBase</c> or <c>UserChat</c> reuse the
/// existing <c>KnowledgeBaseOwnershipGuard</c>/<c>ChatOwnershipGuard</c> instead of duplicating
/// them here (constitution §18) — added to Foundational (rather than deferred to Polish) per
/// <c>/speckit-analyze</c> finding I2, so every mutation command below is ownership-guarded from
/// the moment it's implemented, not after the fact.
/// </summary>
public static class RetrievalOwnershipGuard
{
    /// <summary>FR-045 — an <see cref="IndexingJob"/>'s ownership is derived from its <see cref="KnowledgeBase"/>; throws the same not-found shape as the other guards so denial is indistinguishable from not-found.</summary>
    public static IndexingJob EnsureIndexingJobOwnedBy(IndexingJob? job, KnowledgeBase? knowledgeBase, string userId)
    {
        if (job is null || knowledgeBase is null || job.KnowledgeBaseId != knowledgeBase.Id || !knowledgeBase.IsOwnedBy(userId))
        {
            throw new KeyNotFoundException("Indexing job not found.");
        }

        return job;
    }

    /// <summary>FR-034, FR-048 — a citation is owned by the caller only if the conversation (<see cref="UserChat"/>) its <see cref="Message"/> belongs to is owned by them; prevents a citation-lookup leak across users.</summary>
    public static Citation EnsureCitationOwnedBy(Citation? citation, UserChat? userChat, string userId)
    {
        if (citation is null || userChat is null || !userChat.IsOwnedBy(userId))
        {
            throw new KeyNotFoundException("Citation not found.");
        }

        return citation;
    }
}
