using AskLucy.Application.Abstractions;
using AskLucy.Application.Documents.Authorization;
using AskLucy.Domain.Documents;
using MediatR;

namespace AskLucy.Application.Documents.Commands.AddTag;

/// <summary>FR-032 — tags are shared/reused across a user's documents (data-model.md), so this looks up an existing tag by name before creating a new one, keyed per-owner.</summary>
public sealed class AddTagCommandHandler(
    IDocumentRepository documentRepository,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser) : IRequestHandler<AddTagCommand, IReadOnlyList<string>>
{
    public async Task<IReadOnlyList<string>> Handle(AddTagCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var document = DocumentOwnershipGuard.EnsureOwnedBy(
            await documentRepository.GetByIdAsync(request.DocumentId, cancellationToken), userId);

        var name = request.Name.Trim();
        var tag = await documentRepository.FindTagByOwnerAndNameAsync(userId, name, cancellationToken);
        if (tag is null)
        {
            tag = DocumentTag.Create(userId, name, userId);
            documentRepository.AddTag(tag);
        }

        document.AddTag(tag, userId);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return document.Tags.Select(t => t.Name).ToList();
    }
}
