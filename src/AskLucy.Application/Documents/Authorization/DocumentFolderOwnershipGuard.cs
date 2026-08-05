using AskLucy.Domain.Documents;

namespace AskLucy.Application.Documents.Authorization;

/// <summary>Mirrors <see cref="DocumentOwnershipGuard"/> for <see cref="DocumentFolder"/> (FR-033, FR-048) — folders are single-owner (spec.md Assumptions), no shared/cross-user folders.</summary>
public static class DocumentFolderOwnershipGuard
{
    public static DocumentFolder EnsureOwnedBy(DocumentFolder? folder, string userId)
    {
        if (folder is null || folder.OwnerId != userId)
        {
            throw new KeyNotFoundException("Folder not found.");
        }

        return folder;
    }
}
