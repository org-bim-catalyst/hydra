using AskLucy.Domain.Documents;

namespace AskLucy.Application.Documents.Authorization;

/// <summary>
/// Centralizes the "does this document belong to this caller" check shared by every mutating/
/// read handler (FR-048), instead of duplicating it per handler. A plain static guard rather
/// than an ASP.NET Core <c>IAuthorizationHandler</c> — those types live in
/// <c>Microsoft.AspNetCore.Authorization</c>, which Application must not reference (constitution
/// §3, Dependency Rule). Mirrors <c>KnowledgeBaseOwnershipGuard</c>/<c>ChatOwnershipGuard</c>.
/// </summary>
public static class DocumentOwnershipGuard
{
    public static Document EnsureOwnedBy(Document? document, string userId)
    {
        if (document is null || !document.IsOwnedBy(userId))
        {
            throw new KeyNotFoundException("Document not found.");
        }

        return document;
    }
}
