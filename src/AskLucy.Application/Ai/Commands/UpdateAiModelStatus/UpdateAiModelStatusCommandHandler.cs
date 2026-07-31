using AskLucy.Application.Abstractions;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AskLucy.Application.Ai.Commands.UpdateAiModelStatus;

public sealed class UpdateAiModelStatusCommandHandler(
    IAIModelRepository models,
    IUnitOfWork unitOfWork,
    ICurrentUserAccessor currentUser,
    ILogger<UpdateAiModelStatusCommandHandler> logger) : IRequestHandler<UpdateAiModelStatusCommand>
{
    public async Task Handle(UpdateAiModelStatusCommand request, CancellationToken cancellationToken)
    {
        var actorUserId = currentUser.UserId ?? throw new UnauthorizedAccessException();
        var model = await models.GetByIdAsync(request.ModelId, cancellationToken)
            ?? throw new KeyNotFoundException("Model not found.");

        var oldStatus = model.Status;
        model.SetStatus(request.Status, actorUserId);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        AiAdminActionLog.AdminAiModelStatusChanged(logger, actorUserId, model.Id, oldStatus.ToString(), request.Status.ToString());
    }
}
