using AskLucy.Domain.KnowledgeBases;

namespace AskLucy.Application.KnowledgeBases.Authorization;

/// <summary>
/// Centralizes the "does this knowledge base belong to this caller" check shared by every
/// mutating/read handler (FR-010), instead of duplicating it per handler. A plain static
/// guard rather than an ASP.NET Core <c>IAuthorizationHandler</c> — those types live in
/// <c>Microsoft.AspNetCore.Authorization</c>, which Application must not reference
/// (constitution §3, Dependency Rule). Mirrors <c>ChatOwnershipGuard</c>.
/// </summary>
public static class KnowledgeBaseOwnershipGuard
{
    public static KnowledgeBase EnsureOwnedBy(KnowledgeBase? knowledgeBase, string userId)
    {
        if (knowledgeBase is null || !knowledgeBase.IsOwnedBy(userId))
        {
            throw new KeyNotFoundException("Knowledge base not found.");
        }

        return knowledgeBase;
    }
}
