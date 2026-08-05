using AskLucy.Domain.Documents;

namespace AskLucy.Application.Abstractions;

/// <summary>Aggregate-oriented repository for <see cref="DocumentFolder"/> (constitution §3 Repository rules).</summary>
public interface IDocumentFolderRepository
{
    Task<DocumentFolder?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    void Add(DocumentFolder folder);

    /// <summary>True if <paramref name="candidateAncestorId"/> is <paramref name="folderId"/> itself or one of its descendants — used to reject a circular move (Edge Cases).</summary>
    Task<bool> IsSelfOrDescendantAsync(Guid folderId, Guid candidateAncestorId, CancellationToken cancellationToken = default);

    /// <summary>True if the folder currently contains any (non-deleted) documents — used to require an explicit `onContainedDocuments` choice before delete (Edge Cases).</summary>
    Task<bool> HasDocumentsAsync(Guid folderId, CancellationToken cancellationToken = default);

    void Remove(DocumentFolder folder);

    /// <summary>The caller's full folder hierarchy, for <c>GetFolderTree</c> (FR-033).</summary>
    Task<IReadOnlyList<DocumentFolder>> ListByOwnerAsync(string ownerId, CancellationToken cancellationToken = default);
}
