using AskLucy.Application.Abstractions;
using AskLucy.Application.CustomModels.Abstractions;
using AskLucy.Domain.CustomModels;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AskLucy.Application.CustomModels.Commands.RemoveCustomModel;

/// <summary>
/// specs/072 FR-031. <see cref="CustomModel.Remove"/> refuses any state but Failed or Cancelled (400).
/// specs/078 FR-009b: the model Local Whisper is set to use is refused too (409).
/// </summary>
public sealed class RemoveCustomModelCommandHandler(
    ICustomModelRepository customModels,
    IDictationEngineSettingRepository dictationSettings,
    ICustomModelDeploymentNotifier notifier,
    CustomModelSummaryBuilder summaries,
    ICurrentUserAccessor currentUser,
    TimeProvider timeProvider,
    ILogger<RemoveCustomModelCommandHandler> logger) : IRequestHandler<RemoveCustomModelCommand>
{
    public async Task Handle(RemoveCustomModelCommand request, CancellationToken cancellationToken)
    {
        var actorUserId = currentUser.UserId ?? throw new UnauthorizedAccessException();

        if (await dictationSettings.GetSelectedLocalWhisperModelIdAsync(cancellationToken) == request.Id)
        {
            throw new CustomModelSelectedForLocalWhisperException();
        }

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
