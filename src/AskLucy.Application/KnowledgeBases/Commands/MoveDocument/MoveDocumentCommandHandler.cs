using AskLucy.Application.Abstractions;
using AskLucy.Application.KnowledgeBases.Authorization;
using MediatR;

namespace AskLucy.Application.KnowledgeBases.Commands.MoveDocument;

public sealed class MoveDocumentCommandHandler(
    IKnowledgeBaseRepository knowledgeBaseRepository,
    IKnowledgeBaseFolderRepository folderRepository,
    IKnowledgeBaseDocumentRepository documentRepository,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser) : IRequestHandler<MoveDocumentCommand, KnowledgeBaseDocumentDto>
{
    public async Task<KnowledgeBaseDocumentDto> Handle(MoveDocumentCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        KnowledgeBaseOwnershipGuard.EnsureOwnedBy(await knowledgeBaseRepository.GetByIdAsync(request.KnowledgeBaseId, cancellationToken), userId);
        var document = KnowledgeBaseDocumentGuard.EnsureBelongsTo(
            await documentRepository.GetByIdAsync(request.DocumentId, cancellationToken), request.KnowledgeBaseId);

        if (request.NewFolderId is { } newFolderId)
        {
            KnowledgeBaseFolderGuard.EnsureBelongsTo(await folderRepository.GetByIdAsync(newFolderId, cancellationToken), request.KnowledgeBaseId);
        }

        document.Move(request.NewFolderId, userId);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return KnowledgeBaseDocumentDto.FromEntity(document);
    }
}
