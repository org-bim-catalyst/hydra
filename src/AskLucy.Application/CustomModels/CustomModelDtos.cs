using AskLucy.Application.Users;
using AskLucy.Domain.CustomModels;

namespace AskLucy.Application.CustomModels;

// specs/072 contracts/admin-custom-models.md "Shapes" and contracts/custom-model-deployments-hub.md.
// Enum-valued fields are strings here so the REST and SignalR payloads read the same whatever
// JSON options each serializer runs with. Nothing below carries the deployment target's host,
// username, password or root path (FR-017, FR-018).

public sealed record CustomModelUserDto(string Id, string DisplayName);

public record CustomModelSummaryDto(
    Guid Id,
    string Name,
    string RepositoryId,
    string Revision,
    string? ResolvedCommitSha,
    string SourceUrl,
    string Destination,
    string DeploymentState,
    string Availability,
    bool CanMakeAvailable,
    string? AvailabilityBlockedReason,
    bool CanRemove,
    bool CanCancel,
    long? TotalBytes,
    long TransferredBytes,
    int? TotalFileCount,
    int CompletedFileCount,
    string? CurrentFilePath,
    long? CurrentFileBytes,
    long? CurrentFileTotalBytes,
    int OverwrittenFileCount,
    string? FailureKind,
    string? FailureReason,
    CustomModelUserDto SubmittedBy,
    DateTime CreatedAtUtc,
    DateTime? StartedAtUtc,
    DateTime? FinishedAtUtc,
    string? BacksEngine);

public sealed record OverwrittenFileDto(string RelativePath, long PreviousSizeBytes, DateTime OverwrittenAtUtc);

/// <summary>A summary plus the paged overwritten-file report (FR-010a). Derived, so the JSON is flat like the contract's <c>extends</c>.</summary>
public sealed record CustomModelDetailDto : CustomModelSummaryDto
{
    public CustomModelDetailDto(CustomModelSummaryDto summary, PagedResult<OverwrittenFileDto> overwrittenFiles)
        : base(summary)
    {
        OverwrittenFiles = overwrittenFiles;
    }

    public PagedResult<OverwrittenFileDto> OverwrittenFiles { get; init; }
}

public sealed record DeploymentStatusDto(
    bool IsConfigured,
    string? Transport,
    long MaxDeploymentBytes,
    IReadOnlyList<string> AllowedDestinationPrefixes);

public sealed record SourcePreviewDto(
    bool IsValid,
    string? Error,
    string? RepositoryId,
    string? Revision,
    string? IgnoredFilePath,
    string? DerivedName,
    bool NameAvailable);

public enum CustomModelTransferPhase
{
    Downloading,
    Uploading,
    Verifying,
}

public sealed record OverwroteFileDto(string RelativePath, long PreviousSizeBytes);

public sealed record CustomModelProgressDto(
    Guid CustomModelId,
    string DeploymentState,
    long TransferredBytes,
    long TotalBytes,
    int CompletedFileCount,
    int TotalFileCount,
    string? CurrentFilePath,
    long? CurrentFileBytes,
    long? CurrentFileTotalBytes,
    string Phase,
    OverwroteFileDto? Overwrote,
    DateTime SentAtUtc);

public static class CustomModelDtoMapping
{
    public const string NotCompletedReason = "The deployment has not completed.";

    public static CustomModelSummaryDto ToSummaryDto(this CustomModel model, CustomModelUserDto submittedBy, string? backsEngine)
    {
        var completed = model.DeploymentState == CustomModelDeploymentState.Completed;
        return new CustomModelSummaryDto(
            model.Id,
            model.Name,
            model.RepositoryId,
            model.Revision,
            model.ResolvedCommitSha,
            model.SourceUrl,
            model.Destination,
            model.DeploymentState.ToString(),
            model.Availability.ToString(),
            CanMakeAvailable: completed,
            AvailabilityBlockedReason: completed ? null : NotCompletedReason,
            CanRemove: model.DeploymentState is CustomModelDeploymentState.Failed or CustomModelDeploymentState.Cancelled,
            CanCancel: model.IsInProgress,
            model.TotalBytes,
            model.TransferredBytes,
            model.TotalFileCount,
            model.CompletedFileCount,
            model.CurrentFilePath,
            model.CurrentFileBytes,
            model.CurrentFileTotalBytes,
            model.OverwrittenFileCount,
            model.FailureKind?.ToString(),
            model.FailureReason,
            submittedBy,
            model.CreatedAtUtc,
            model.StartedAtUtc,
            model.FinishedAtUtc,
            backsEngine);
    }

    public static OverwrittenFileDto ToDto(this CustomModelOverwrittenFile file) =>
        new(file.RelativePath, file.PreviousSizeBytes, file.OverwrittenAtUtc);
}
