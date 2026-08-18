using AskLucy.Application.Abstractions;
using AskLucy.Application.Prompts.Authorization;
using MediatR;

namespace AskLucy.Application.Prompts.Commands.SetPinned;

public sealed class SetPinnedCommandHandler(
    IPromptRepository promptRepository, IUnitOfWork unitOfWork, ICurrentUserAccessor currentUser)
    : IRequestHandler<SetPinnedCommand>
{
    public async Task Handle(SetPinnedCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        var prompt = PromptOwnershipGuard.EnsureOwnedBy(
            await promptRepository.GetByIdForOwnerAsync(request.PromptId, userId, cancellationToken), userId);

        prompt.SetPinned(request.IsPinned, userId);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
