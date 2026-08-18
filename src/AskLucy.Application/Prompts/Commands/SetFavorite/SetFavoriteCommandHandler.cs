using AskLucy.Application.Abstractions;
using AskLucy.Application.Prompts.Authorization;
using MediatR;

namespace AskLucy.Application.Prompts.Commands.SetFavorite;

public sealed class SetFavoriteCommandHandler(
    IPromptRepository promptRepository, IUnitOfWork unitOfWork, ICurrentUserAccessor currentUser)
    : IRequestHandler<SetFavoriteCommand>
{
    public async Task Handle(SetFavoriteCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        var prompt = PromptOwnershipGuard.EnsureOwnedBy(
            await promptRepository.GetByIdForOwnerAsync(request.PromptId, userId, cancellationToken), userId);

        prompt.SetFavorite(request.IsFavorite, userId);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
