using AskLucy.Application.CustomModels;
using AskLucy.Application.CustomModels.Abstractions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace AskLucy.Infrastructure.CustomModels;

/// <summary>
/// specs/072 research D8. Pure push over <see cref="CustomModelDeploymentHub"/>. A failed push is
/// logged and swallowed by contract: the change it reports is already saved, and clients refetch
/// on reconnect (FR-024), so a dropped event must not stop a transfer.
/// </summary>
public sealed partial class CustomModelDeploymentNotifier(
    IHubContext<CustomModelDeploymentHub> hubContext,
    ILogger<CustomModelDeploymentNotifier> logger) : ICustomModelDeploymentNotifier
{
    public const string ProgressEvent = "CustomModelDeploymentProgress";

    public const string StateChangedEvent = "CustomModelDeploymentStateChanged";

    public async Task NotifyProgressAsync(CustomModelProgressDto progress, CancellationToken cancellationToken = default)
    {
        try
        {
            await hubContext.Clients.Group(CustomModelDeploymentHub.ViewersGroup).SendAsync(ProgressEvent, progress, cancellationToken);
        }
        catch (Exception ex)
        {
            LogPushFailed(logger, ex, ProgressEvent, progress.CustomModelId);
        }
    }

    public async Task NotifyStateChangedAsync(CustomModelSummaryDto model, bool removed = false, CancellationToken cancellationToken = default)
    {
        try
        {
            await hubContext.Clients.Group(CustomModelDeploymentHub.ViewersGroup).SendAsync(StateChangedEvent, new CustomModelStateChangedEvent(model, removed), cancellationToken);
        }
        catch (Exception ex)
        {
            LogPushFailed(logger, ex, StateChangedEvent, model.Id);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't push {EventName} for custom model {CustomModelId}; clients refetch on reconnect")]
    private static partial void LogPushFailed(ILogger logger, Exception exception, string eventName, Guid customModelId);
}

/// <summary>The summary, flattened, plus <c>removed</c> (contracts/custom-model-deployment-hub.md).</summary>
public sealed record CustomModelStateChangedEvent : CustomModelSummaryDto
{
    public CustomModelStateChangedEvent(CustomModelSummaryDto summary, bool removed)
        : base(summary)
    {
        Removed = removed;
    }

    public bool Removed { get; init; }
}
