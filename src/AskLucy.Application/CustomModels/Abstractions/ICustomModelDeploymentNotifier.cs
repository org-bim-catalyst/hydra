namespace AskLucy.Application.CustomModels.Abstractions;

/// <summary>
/// specs/072 research D8. Pushes deployment events to admins holding <c>admin.custom-models.view</c>.
/// Called only after the change it reports has been saved. Implementations log a failed push and
/// never throw: a dropped event must not stop a transfer, and clients refetch on reconnect (FR-024).
/// </summary>
public interface ICustomModelDeploymentNotifier
{
    Task NotifyProgressAsync(CustomModelProgressDto progress, CancellationToken cancellationToken = default);

    Task NotifyStateChangedAsync(CustomModelSummaryDto model, bool removed = false, CancellationToken cancellationToken = default);
}
