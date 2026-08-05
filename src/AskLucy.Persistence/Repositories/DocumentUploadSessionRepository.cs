using AskLucy.Application.Abstractions;
using AskLucy.Domain.Documents;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Repositories;

public sealed class DocumentUploadSessionRepository(AskLucyDbContext dbContext) : IDocumentUploadSessionRepository
{
    public Task<DocumentUploadSession?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.DocumentUploadSessions.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

    public Task<DocumentUploadSession?> GetInProgressForDocumentAsync(Guid documentId, CancellationToken cancellationToken = default) =>
        dbContext.DocumentUploadSessions.FirstOrDefaultAsync(
            s => s.TargetDocumentId == documentId && s.Status == DocumentUploadSessionStatus.InProgress, cancellationToken);

    public void Add(DocumentUploadSession session) => dbContext.DocumentUploadSessions.Add(session);
}
