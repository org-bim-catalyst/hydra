using AskLucy.Application.Abstractions;
using AskLucy.Application.Prompts.Authorization;
using MediatR;

namespace AskLucy.Application.Prompts.Commands.RemoveTag;

public sealed class RemoveTagCommandHandler(
    IPromptRepository promptRepository, IUnitOfWork unitOfWork, ICurrentUserAccessor currentUser)
    : IRequestHandler<RemoveTagCommand>
{
    public async Task Handle(RemoveTagCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        var prompt = PromptOwnershipGuard.EnsureOwnedBy(
            await promptRepository.GetByIdForOwnerAsync(request.PromptId, userId, cancellationToken), userId);

        prompt.RemoveTag(request.TagId, userId);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
