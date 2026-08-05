using AskLucy.Application.Abstractions;
using AskLucy.Application.Documents.Authorization;
using MediatR;

namespace AskLucy.Application.Documents.Commands.CancelUpload;

/// <summary>Cancels an in-progress upload (FR-007) — deletes already-received chunks, leaving no orphaned partial file (Edge Cases).</summary>
public sealed class CancelUploadCommandHandler(
    IDocumentUploadSessionRepository sessionRepository,
    IResumableUploadStorage resumableStorage,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser) : IRequestHandler<CancelUploadCommand>
{
    public async Task Handle(CancelUploadCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var session = DocumentUploadSessionGuard.EnsureOwnedBy(
            await sessionRepository.GetByIdAsync(request.UploadSessionId, cancellationToken), userId);

        session.Cancel(userId);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await resumableStorage.DeleteAsync(session.Id.ToString(), cancellationToken);
    }
}
