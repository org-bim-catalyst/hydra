using AskLucy.Domain.Documents;

namespace AskLucy.Application.Documents.Authorization;

/// <summary>Mirrors <see cref="DocumentOwnershipGuard"/> for <see cref="DocumentUploadSession"/> — denial is indistinguishable from not-found.</summary>
public static class DocumentUploadSessionGuard
{
    public static DocumentUploadSession EnsureOwnedBy(DocumentUploadSession? session, string userId)
    {
        if (session is null || !session.IsOwnedBy(userId))
        {
            throw new KeyNotFoundException("Upload session not found.");
        }

        return session;
    }
}
