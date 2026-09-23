using AskLucy.Domain.CustomModels;

namespace AskLucy.Application.CustomModels.Abstractions;

/// <summary>What the job writes on each progress flush (FR-021). Forward-only: a lower <see cref="TransferredBytes"/> than the stored one is ignored.</summary>
public sealed record CustomModelProgress(
    long TransferredBytes,
    int CompletedFileCount,
    string? CurrentFilePath,
    long? CurrentFileBytes,
    long? CurrentFileTotalBytes);

/// <summary>Research D7. The job's per-flush read of whether it should keep going.</summary>
public enum CustomModelRunSignal
{
    Continue,
    CancellationRequested,

    /// <summary>The record is terminal (for example the recovery sweep failed it) or gone. The job stops without writing a terminal state of its own.</summary>
    NoLongerInProgress,
}

/// <summary>
/// specs/072. Persistence for <see cref="CustomModel"/>. Saves translate a unique-index violation into
/// <c>DuplicateResourceException</c> naming the field, and a second RowVersion conflict into
/// <c>ConcurrencyConflictException</c> — Application never sees an EF Core type.
/// </summary>
public interface ICustomModelRepository
{
    /// <summary>Adds and saves a new model.</summary>
    Task AddAsync(CustomModel model, CancellationToken cancellationToken = default);

    /// <summary>A tracked, non-deleted model, or <see langword="null"/>.</summary>
    Task<CustomModel?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads the model, applies <paramref name="apply"/>, and saves. On a RowVersion conflict (a
    /// progress write bumped the row) it reloads and applies once more; a second conflict throws
    /// (constitution §2 VIII, research D6). <paramref name="apply"/> returns <see langword="false"/>
    /// when the change no longer applies (for example the record is no longer in progress), in which
    /// case nothing is saved. Returns the saved model, or <see langword="null"/> when it wasn't found
    /// or <paramref name="apply"/> declined.
    /// </summary>
    Task<CustomModel?> UpdateAsync(Guid id, Func<CustomModel, bool> apply, CancellationToken cancellationToken = default);

    /// <summary>Newest first (research D13). Not tracked.</summary>
    Task<(IReadOnlyList<CustomModel> Items, int TotalCount)> ListAsync(int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>Oldest first. Not tracked.</summary>
    Task<(IReadOnlyList<CustomModelOverwrittenFile> Items, int TotalCount)> GetOverwrittenFilesAsync(
        Guid customModelId, int page, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>FR-032. Among non-deleted models, ignoring case.</summary>
    Task<bool> NameExistsAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>FR-015, research D5. The in-progress model whose destination equals, contains or is contained by <paramref name="destination"/> on a segment boundary, ignoring case.</summary>
    Task<CustomModel?> FindActiveJobOverlappingDestinationAsync(string destination, CancellationToken cancellationToken = default);

    /// <summary>FR-034. The non-deleted Available model for the repository, ignoring case.</summary>
    Task<CustomModel?> FindAvailableForRepositoryAsync(string repositoryId, CancellationToken cancellationToken = default);

    /// <summary>FR-039. Non-deleted <see cref="CustomModelDeploymentState.Completed"/> models for the repository, ignoring case. Not tracked.</summary>
    Task<IReadOnlyList<CustomModel>> FindCompletedForRepositoryAsync(string repositoryId, CancellationToken cancellationToken = default);

    /// <summary>One forward-only <c>UPDATE</c> (<c>WHERE TransferredBytes &lt;= @new</c>), only while transferring. Bumps the RowVersion.</summary>
    Task UpdateProgressAsync(Guid id, CustomModelProgress progress, CancellationToken cancellationToken = default);

    /// <summary>Inserts the row <see cref="CustomModel.RecordOverwrite"/> returned and increments the stored count, in one transaction.</summary>
    Task AddOverwrittenFileAsync(CustomModelOverwrittenFile file, CancellationToken cancellationToken = default);

    Task<CustomModelRunSignal> GetRunSignalAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Every in-progress model, <see cref="CustomModelDeploymentState.Queued"/> included — for the startup recovery sweep. Not tracked.</summary>
    Task<IReadOnlyList<CustomModel>> ListInProgressAsync(CancellationToken cancellationToken = default);
}
