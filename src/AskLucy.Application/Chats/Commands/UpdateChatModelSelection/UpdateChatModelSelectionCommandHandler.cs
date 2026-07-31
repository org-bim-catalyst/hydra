using System.Text.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Chats.Authorization;
using MediatR;

namespace AskLucy.Application.Chats.Commands.UpdateChatModelSelection;

public sealed class UpdateChatModelSelectionCommandHandler(
    IUserChatRepository chatRepository,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser) : IRequestHandler<UpdateChatModelSelectionCommand>
{
    public async Task Handle(UpdateChatModelSelectionCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var chat = ChatOwnershipGuard.EnsureOwnedBy(
            await chatRepository.GetByIdAsync(request.ChatId, cancellationToken), userId);

        var generationParametersJson = request.GenerationParameters is null
            ? null
            : JsonSerializer.Serialize(request.GenerationParameters);

        chat.SetModelSelection(request.ProviderId, request.ModelId, generationParametersJson, userId);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
