using AskLucy.Domain.Documents;

namespace AskLucy.Application.Abstractions;

public interface IDocumentUploadSessionRepository
{
    Task<DocumentUploadSession?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>US5's replace-version in-flight conflict check (Edge Cases, contracts/document-versions-folders-api.md) — an `InProgress` session whose <see cref="DocumentUploadSession.TargetDocumentId"/> is <paramref name="documentId"/>, or null if none.</summary>
    Task<DocumentUploadSession?> GetInProgressForDocumentAsync(Guid documentId, CancellationToken cancellationToken = default);

    void Add(DocumentUploadSession session);
}
