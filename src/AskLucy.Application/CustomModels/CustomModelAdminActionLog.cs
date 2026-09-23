using AskLucy.Domain.CustomModels;
using Microsoft.Extensions.Logging;

namespace AskLucy.Application.CustomModels;

/// <summary>
/// specs/072 FR-027 research D12. Structured security events for every custom-model action,
/// following <see cref="Ai.AiAdminActionLog"/>. Events carry only the actor, the model, its name,
/// repository-relative paths, a failure kind and a safe reason — never the deployment target's
/// password, host or root path.
/// </summary>
internal static partial class CustomModelAdminActionLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Custom model {CustomModelId} ({Name}) submitted by {ActorUserId} from {RepositoryId}@{Revision} to {Destination}")]
    public static partial void Submitted(ILogger logger, string actorUserId, Guid customModelId, string name, string repositoryId, string revision, string destination);

    [LoggerMessage(Level = LogLevel.Information, Message = "Cancellation of custom model {CustomModelId} ({Name}) requested by {ActorUserId}")]
    public static partial void CancelRequested(ILogger logger, string actorUserId, Guid customModelId, string name);

    [LoggerMessage(Level = LogLevel.Information, Message = "Custom model {CustomModelId} ({Name}) deployment cancelled after {CompletedFileCount} files")]
    public static partial void Cancelled(ILogger logger, Guid customModelId, string name, int completedFileCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Custom model {CustomModelId} ({Name}) deployed:{FileCount} files, {TotalBytes} bytes, {OverwrittenFileCount} overwritten")]
    public static partial void Completed(ILogger logger, Guid customModelId, string name, int fileCount, long totalBytes, int overwrittenFileCount);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Custom model {CustomModelId} ({Name}) deployment ended {DeploymentState} with {FailureKind}: {Reason}")]
    public static partial void Failed(ILogger logger, Exception? exception, Guid customModelId, string name, CustomModelDeploymentState deploymentState, CustomModelFailureKind? failureKind, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Custom model {CustomModelId} ({Name}) overwrote {RelativePath} (previously {PreviousSizeBytes} bytes)")]
    public static partial void FileOverwritten(ILogger logger, Guid customModelId, string name, string relativePath, long previousSizeBytes);

    [LoggerMessage(Level = LogLevel.Information, Message = "Custom model {CustomModelId} ({Name}) availability changed by {ActorUserId}: {OldAvailability} -> {NewAvailability}")]
    public static partial void AvailabilityChanged(ILogger logger, string actorUserId, Guid customModelId, string name, CustomModelAvailability oldAvailability, CustomModelAvailability newAvailability);

    [LoggerMessage(Level = LogLevel.Information, Message = "Custom model {CustomModelId} ({Name}) removed by {ActorUserId}; its files on the deployment target were left in place")]
    public static partial void Removed(ILogger logger, string actorUserId, Guid customModelId, string name);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Custom model {CustomModelId} is being deployed over plain FTP because Ftp:AllowPlainFtp is set; credentials and files travel unencrypted")]
    public static partial void PlainFtpInUse(ILogger logger, Guid customModelId);
}
