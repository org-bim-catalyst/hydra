using System.Globalization;
using System.Text.RegularExpressions;
using AskLucy.Domain.Common;

namespace AskLucy.Domain.CustomModels;

/// <summary>
/// specs/072 data-model.md. A model an admin deployed from Hugging Face to the deployment target,
/// and the single deployment that put it there: the spec rules out redeploying, so one record is
/// exactly one deployment. Every method below is a state-machine transition; an illegal one throws
/// <see cref="DomainRuleViolationException"/>.
/// </summary>
/// <remarks>
/// Progress (<see cref="TransferredBytes"/>, <see cref="CompletedFileCount"/>, the current-file
/// fields) and overwritten-file rows are written straight to the table by the job, bypassing
/// this aggregate, because they change many times a second. <see cref="RecordProgress"/> and
/// <see cref="RecordOverwrite"/> still own the rules for the job's in-memory copy.
/// </remarks>
public sealed partial class CustomModel : BaseEntity
{
    public const int MaxNameLength = 100;
    public const int MaxRepositoryIdLength = 200;
    public const int MaxRevisionLength = 255;
    public const int MaxSourceUrlLength = 2048;
    public const int MaxFailureReasonLength = 1000;
    public const int MaxCurrentFilePathLength = 1024;
    public const int MaxBackgroundJobIdLength = 100;

    public string Name { get; private set; } = string.Empty;

    /// <summary><c>owner/repo</c>. Case-insensitive everywhere; see <see cref="CanonicaliseRepositoryId"/>.</summary>
    public string RepositoryId { get; private set; } = string.Empty;

    public string Revision { get; private set; } = string.Empty;

    /// <summary>The 40-hex commit every file was fetched from. Null until listing succeeds.</summary>
    public string? ResolvedCommitSha { get; private set; }

    /// <summary>Display only — never fetched (research D4).</summary>
    public string SourceUrl { get; private set; } = string.Empty;

    /// <summary>Canonical, relative to the deployment root, which it never contains.</summary>
    public string Destination { get; private set; } = string.Empty;

    public CustomModelDeploymentState DeploymentState { get; private set; }

    public CustomModelAvailability Availability { get; private set; }

    public long? TotalBytes { get; private set; }

    public long TransferredBytes { get; private set; }

    public int? TotalFileCount { get; private set; }

    public int CompletedFileCount { get; private set; }

    public string? CurrentFilePath { get; private set; }

    public long? CurrentFileBytes { get; private set; }

    public long? CurrentFileTotalBytes { get; private set; }

    public CustomModelFailureKind? FailureKind { get; private set; }

    /// <summary>Safe, human-readable. Never contains a secret or the deployment root (FR-017, FR-018).</summary>
    public string? FailureReason { get; private set; }

    public string SubmittedByUserId { get; private set; } = string.Empty;

    public DateTime? CancellationRequestedAtUtc { get; private set; }

    public string? CancelledByUserId { get; private set; }

    /// <summary>The Hangfire job id, so a cancelled <see cref="CustomModelDeploymentState.Queued"/> job can be deleted.</summary>
    public string? BackgroundJobId { get; private set; }

    public DateTime? StartedAtUtc { get; private set; }

    public DateTime? FinishedAtUtc { get; private set; }

    /// <summary>Persisted so the filtered unique index on <see cref="Destination"/> can use it. Only ever set by the transitions below.</summary>
    public bool IsInProgress { get; private set; }

    public int OverwrittenFileCount { get; private set; }

    private CustomModel()
    {
        // Required by EF Core materialization.
    }

    public static CustomModel Create(string name, HuggingFaceModelSource source, DeploymentDestination destination, string submittedByUserId)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);

        var trimmedName = name?.Trim() ?? string.Empty;
        if (!IsValidName(trimmedName))
        {
            throw new DomainRuleViolationException($"A model name is required, must be at most {MaxNameLength} characters, and must not contain control characters.");
        }

        if (string.IsNullOrWhiteSpace(submittedByUserId))
        {
            throw new DomainRuleViolationException("A custom model must record the admin who submitted it.");
        }

        return new CustomModel
        {
            Id = Guid.CreateVersion7(),
            Name = trimmedName,
            RepositoryId = source.RepositoryId,
            Revision = source.Revision,
            SourceUrl = source.SourceUrl,
            Destination = destination.Value,
            DeploymentState = CustomModelDeploymentState.Queued,
            Availability = CustomModelAvailability.Unavailable,
            SubmittedByUserId = submittedByUserId,
            IsInProgress = true,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = submittedByUserId,
        };
    }

    /// <summary>FR-002. Trimmed, 1–<see cref="MaxNameLength"/> characters, no control characters.</summary>
    public static bool IsValidName(string? name)
    {
        var trimmed = name?.Trim();
        return !string.IsNullOrEmpty(trimmed) && trimmed.Length <= MaxNameLength && !trimmed.Any(char.IsControl);
    }

    public void AssignBackgroundJob(string backgroundJobId)
    {
        Require(DeploymentState == CustomModelDeploymentState.Queued, "A background job can only be assigned while the deployment is queued.");
        BackgroundJobId = backgroundJobId;
    }

    public void StartListing(DateTime utcNow)
    {
        Require(DeploymentState == CustomModelDeploymentState.Queued, "Only a queued deployment can start listing files.");
        DeploymentState = CustomModelDeploymentState.Listing;
        StartedAtUtc = utcNow;
    }

    /// <summary>Research D3 step 4 — adopts Hugging Face's casing of the same repository.</summary>
    public void CanonicaliseRepositoryId(string canonicalRepositoryId)
    {
        Require(DeploymentState == CustomModelDeploymentState.Listing, "The repository id can only be canonicalised while listing files.");
        Require(
            string.Equals(canonicalRepositoryId, RepositoryId, StringComparison.OrdinalIgnoreCase),
            "Hugging Face returned a different repository from the one requested.");
        RepositoryId = canonicalRepositoryId;
    }

    /// <summary>FR-008. The cap is passed in by the caller; the Domain doesn't read configuration.</summary>
    public void BeginTransfer(string commitSha, long totalBytes, int fileCount, long maxDeploymentBytes)
    {
        Require(DeploymentState == CustomModelDeploymentState.Listing, "Only a deployment that is listing files can begin transferring.");
        Require(CommitShaPattern().IsMatch(commitSha ?? string.Empty), "The resolved commit must be a 40-character lowercase hexadecimal SHA.");
        Require(totalBytes >= 0 && fileCount >= 0, "The file count and total size cannot be negative.");

        if (totalBytes > maxDeploymentBytes)
        {
            throw new CustomModelDeploymentFailedException(
                CustomModelFailureKind.SizeLimitExceeded,
                $"The repository is {FormatBytes(totalBytes)}, which is over the {FormatBytes(maxDeploymentBytes)} deployment limit.");
        }

        ResolvedCommitSha = commitSha;
        TotalBytes = totalBytes;
        TotalFileCount = fileCount;
        TransferredBytes = 0;
        CompletedFileCount = 0;
        DeploymentState = CustomModelDeploymentState.Transferring;
    }

    /// <summary>Forward-only, matching the repository's <c>WHERE TransferredBytes &lt;= @new</c>. A stale update is ignored, never an error.</summary>
    public void RecordProgress(long transferredBytes, int completedFileCount, string? currentFilePath, long? currentFileBytes, long? currentFileTotalBytes)
    {
        Require(DeploymentState == CustomModelDeploymentState.Transferring, "Progress can only be recorded while transferring.");
        if (transferredBytes < TransferredBytes)
        {
            return;
        }

        TransferredBytes = transferredBytes;
        CompletedFileCount = Math.Max(CompletedFileCount, completedFileCount);
        CurrentFilePath = currentFilePath;
        CurrentFileBytes = currentFileBytes;
        CurrentFileTotalBytes = currentFileTotalBytes;
    }

    /// <summary>FR-010a. Returns the row for the job to append; the aggregate keeps only the count.</summary>
    public CustomModelOverwrittenFile RecordOverwrite(string relativePath, long previousSizeBytes, DateTime utcNow)
    {
        Require(DeploymentState == CustomModelDeploymentState.Transferring, "Overwritten files can only be recorded while transferring.");
        Require(!string.IsNullOrWhiteSpace(relativePath) && relativePath.Length <= CustomModelOverwrittenFile.MaxRelativePathLength, "An overwritten file needs a relative path.");

        OverwrittenFileCount++;
        return CustomModelOverwrittenFile.Create(Id, relativePath, previousSizeBytes, utcNow);
    }

    public void Complete(DateTime utcNow)
    {
        Require(DeploymentState == CustomModelDeploymentState.Transferring, "Only a transferring deployment can complete.");
        Require(CompletedFileCount == TotalFileCount, "A deployment can only complete once every file has been transferred.");

        DeploymentState = CustomModelDeploymentState.Completed;
        End(utcNow);
    }

    public void Fail(CustomModelFailureKind kind, string reason, DateTime utcNow)
    {
        Require(IsInProgress, "Only a deployment in progress can fail.");

        var safeReason = string.IsNullOrWhiteSpace(reason) ? "The deployment failed." : reason.Trim();
        FailureKind = kind;
        FailureReason = safeReason.Length > MaxFailureReasonLength ? safeReason[..MaxFailureReasonLength] : safeReason;
        DeploymentState = CustomModelDeploymentState.Failed;
        End(utcNow);
    }

    /// <summary>FR-023. A queued deployment has nothing running, so it is cancelled at once; a running one is flagged and the job stops at its next check.</summary>
    public void RequestCancellation(string userId, DateTime utcNow)
    {
        Require(IsInProgress, "Only a deployment in progress can be cancelled.");
        if (CancellationRequestedAtUtc is null)
        {
            CancellationRequestedAtUtc = utcNow;
            CancelledByUserId = userId;
        }

        if (DeploymentState == CustomModelDeploymentState.Queued)
        {
            DeploymentState = CustomModelDeploymentState.Cancelled;
            End(utcNow);
        }
    }

    public void MarkCancelled(DateTime utcNow)
    {
        Require(
            DeploymentState is CustomModelDeploymentState.Listing or CustomModelDeploymentState.Transferring,
            "Only a running deployment can be marked cancelled.");
        Require(CancellationRequestedAtUtc is not null, "A deployment can only be marked cancelled after cancellation was requested.");

        DeploymentState = CustomModelDeploymentState.Cancelled;
        End(utcNow);
    }

    /// <summary>FR-030. The one-Available-per-repository rule spans aggregates, so the handler checks it and a filtered unique index backstops it.</summary>
    public void MakeAvailable()
    {
        Require(DeploymentState == CustomModelDeploymentState.Completed, "Only a completed model can be made available.");
        Availability = CustomModelAvailability.Available;
    }

    public void MakeUnavailable() => Availability = CustomModelAvailability.Unavailable;

    /// <summary>FR-031. Soft delete, which frees the name. The files on the target are left as they are.</summary>
    public void Remove(string userId, DateTime utcNow)
    {
        Require(
            DeploymentState is CustomModelDeploymentState.Failed or CustomModelDeploymentState.Cancelled,
            "Only a failed or cancelled model can be removed.");
        DeletedAtUtc = utcNow;
        DeletedBy = userId;
    }

    private void End(DateTime utcNow)
    {
        IsInProgress = false;
        FinishedAtUtc = utcNow;
        CurrentFilePath = null;
        CurrentFileBytes = null;
        CurrentFileTotalBytes = null;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new DomainRuleViolationException(message);
        }
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1L << 30 => string.Create(CultureInfo.InvariantCulture, $"{bytes / (double)(1L << 30):0.##} GB"),
        >= 1L << 20 => string.Create(CultureInfo.InvariantCulture, $"{bytes / (double)(1L << 20):0.##} MB"),
        >= 1L << 10 => string.Create(CultureInfo.InvariantCulture, $"{bytes / (double)(1L << 10):0.##} KB"),
        _ => string.Create(CultureInfo.InvariantCulture, $"{bytes} bytes"),
    };

    [GeneratedRegex("^[0-9a-f]{40}$", RegexOptions.CultureInvariant)]
    private static partial Regex CommitShaPattern();
}
