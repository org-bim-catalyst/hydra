using AskLucy.Application.Abstractions;
using AskLucy.Application.Prompts.Authorization;
using MediatR;

namespace AskLucy.Application.Prompts.Commands.AddTag;

public sealed class AddTagCommandHandler(
    IPromptRepository promptRepository, IUnitOfWork unitOfWork, ICurrentUserAccessor currentUser)
    : IRequestHandler<AddTagCommand, Guid>
{
    public async Task<Guid> Handle(AddTagCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        var prompt = PromptOwnershipGuard.EnsureOwnedBy(
            await promptRepository.GetByIdForOwnerAsync(request.PromptId, userId, cancellationToken), userId);

        var tag = prompt.AddTag(request.Value, userId, userId);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return tag.Id;
    }
}
