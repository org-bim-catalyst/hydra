using AskLucy.Application.Abstractions;
using AskLucy.Application.Documents.Authorization;
using MediatR;

namespace AskLucy.Application.Documents.Commands.RemoveTag;

/// <summary>
/// FR-032 — detaches the tag from this document only; the shared <c>DocumentTag</c> row itself
/// is never deleted here (it may still be attached to the owner's other documents).
/// </summary>
public sealed class RemoveTagCommandHandler(
    IDocumentRepository documentRepository,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser) : IRequestHandler<RemoveTagCommand>
{
    public async Task Handle(RemoveTagCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var document = DocumentOwnershipGuard.EnsureOwnedBy(
            await documentRepository.GetByIdAsync(request.DocumentId, cancellationToken), userId);

        var tag = await documentRepository.FindTagByOwnerAndNameAsync(userId, request.Name.Trim(), cancellationToken);
        if (tag is null)
        {
            return;
        }

        document.RemoveTag(tag, userId);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
