using AskLucy.Application.Abstractions;
using AskLucy.Application.CustomModels.Abstractions;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AskLucy.Application.CustomModels.Commands.RemoveCustomModel;

/// <summary>specs/072 FR-031. <see cref="Domain.CustomModels.CustomModel.Remove"/> refuses any state but Failed or Cancelled (400).</summary>
public sealed class RemoveCustomModelCommandHandler(
    ICustomModelRepository customModels,
    ICustomModelDeploymentNotifier notifier,
    CustomModelSummaryBuilder summaries,
    ICurrentUserAccessor currentUser,
    TimeProvider timeProvider,
    ILogger<RemoveCustomModelCommandHandler> logger) : IRequestHandler<RemoveCustomModelCommand>
{
    public async Task Handle(RemoveCustomModelCommand request, CancellationToken cancellationToken)
    {
        var actorUserId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        var removed = await customModels.UpdateAsync(
            request.Id,
            m =>
            {
                m.Remove(actorUserId, timeProvider.GetUtcNow().UtcDateTime);
                return true;
            },
            cancellationToken) ?? throw new KeyNotFoundException("The custom model wasn't found.");

        CustomModelAdminActionLog.Removed(logger, actorUserId, removed.Id, removed.Name);

        var summary = await summaries.BuildAsync(removed, cancellationToken);
        await notifier.NotifyStateChangedAsync(summary, removed: true, cancellationToken);
    }
}
