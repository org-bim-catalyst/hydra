using AskLucy.Application.CustomModels.Abstractions;
using AskLucy.Domain.Common;
using AskLucy.Domain.CustomModels;
using AskLucy.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Repositories;

/// <summary>
/// specs/072. Progress and overwrite bookkeeping go straight to SQL (<c>ExecuteUpdateAsync</c>) so a
/// flush never loads the row or competes with an admin's save for the change tracker; every
/// state transition goes through <see cref="UpdateAsync"/>, which reloads first and retries once on
/// the RowVersion those writes bump (research D6).
/// </summary>
public sealed class CustomModelRepository(AskLucyDbContext dbContext) : ICustomModelRepository
{
    public async Task AddAsync(CustomModel model, CancellationToken cancellationToken = default)
    {
        dbContext.CustomModels.Add(model);
        await SaveAsync(cancellationToken);
    }

    public Task<CustomModel?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.CustomModels.FirstOrDefaultAsync(m => m.Id == id, cancellationToken);

    public async Task<CustomModel?> UpdateAsync(Guid id, Func<CustomModel, bool> apply, CancellationToken cancellationToken = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            var model = await LoadCurrentAsync(id, cancellationToken);
            if (model is null || !apply(model))
            {
                return null;
            }

            try
            {
                await SaveAsync(cancellationToken);
                return model;
            }
            catch (DbUpdateConcurrencyException) when (attempt == 1)
            {
                // A progress flush or another admin's save landed between the load and this save.
                // LoadCurrentAsync discards the stale values and the change is re-applied to fresh ones.
            }
            catch (DbUpdateConcurrencyException)
            {
                await LoadCurrentAsync(id, cancellationToken);
                throw new ConcurrencyConflictException("The custom model changed again while this change was being saved. Refresh and try again.");
            }
        }
    }

    public async Task<(IReadOnlyList<CustomModel> Items, int TotalCount)> ListAsync(int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = dbContext.CustomModels.AsNoTracking();
        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(m => m.CreatedAtUtc)
            .ThenByDescending(m => m.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    public async Task<(IReadOnlyList<CustomModelOverwrittenFile> Items, int TotalCount)> GetOverwrittenFilesAsync(
        Guid customModelId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = dbContext.CustomModelOverwrittenFiles.AsNoTracking().Where(f => f.CustomModelId == customModelId);
        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(f => f.OverwrittenAtUtc)
            .ThenBy(f => f.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    // The case-insensitive column collation does the case folding.
    public Task<bool> NameExistsAsync(string name, CancellationToken cancellationToken = default)
    {
        var trimmed = name.Trim();
        return dbContext.CustomModels.AnyAsync(m => m.Name == trimmed, cancellationToken);
    }

    public async Task<CustomModel?> FindActiveJobOverlappingDestinationAsync(string destination, CancellationToken cancellationToken = default)
    {
        // Segment-boundary overlap isn't expressible as a sargable predicate, and only a handful of
        // deployments are ever in progress at once, so they're compared in memory.
        var inProgress = await dbContext.CustomModels.AsNoTracking()
            .Where(m => m.IsInProgress)
            .ToListAsync(cancellationToken);
        return inProgress.FirstOrDefault(m => DeploymentDestination.Overlaps(m.Destination, destination));
    }

    public Task<CustomModel?> FindAvailableForRepositoryAsync(string repositoryId, CancellationToken cancellationToken = default) =>
        dbContext.CustomModels.AsNoTracking()
            .FirstOrDefaultAsync(m => m.RepositoryId == repositoryId && m.Availability == CustomModelAvailability.Available, cancellationToken);

    public async Task<IReadOnlyList<CustomModel>> FindCompletedForRepositoryAsync(string repositoryId, CancellationToken cancellationToken = default) =>
        await dbContext.CustomModels.AsNoTracking()
            .Where(m => m.RepositoryId == repositoryId && m.DeploymentState == CustomModelDeploymentState.Completed)
            .OrderByDescending(m => m.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    public Task UpdateProgressAsync(Guid id, CustomModelProgress progress, CancellationToken cancellationToken = default) =>
        dbContext.CustomModels
            .Where(m => m.Id == id
                && m.DeploymentState == CustomModelDeploymentState.Transferring
                && m.TransferredBytes <= progress.TransferredBytes
                && m.CompletedFileCount <= progress.CompletedFileCount)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(m => m.TransferredBytes, progress.TransferredBytes)
                    .SetProperty(m => m.CompletedFileCount, progress.CompletedFileCount)
                    .SetProperty(m => m.CurrentFilePath, progress.CurrentFilePath)
                    .SetProperty(m => m.CurrentFileBytes, progress.CurrentFileBytes)
                    .SetProperty(m => m.CurrentFileTotalBytes, progress.CurrentFileTotalBytes),
                cancellationToken);

    public async Task AddOverwrittenFileAsync(CustomModelOverwrittenFile file, CancellationToken cancellationToken = default)
    {
        await using (var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken))
        {
            // Raw INSERT rather than Add + SaveChanges: SaveChanges would also flush whatever the
            // tracked aggregate is carrying (including the count RecordOverwrite just bumped in memory).
            await dbContext.Database.ExecuteSqlAsync(
                $"""
                INSERT INTO [CustomModelOverwrittenFiles] ([CustomModelId], [RelativePath], [PreviousSizeBytes], [OverwrittenAtUtc])
                VALUES ({file.CustomModelId}, {file.RelativePath}, {file.PreviousSizeBytes}, {file.OverwrittenAtUtc})
                """,
                cancellationToken);

            await dbContext.CustomModels
                .IgnoreQueryFilters()
                .Where(m => m.Id == file.CustomModelId)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.OverwrittenFileCount, m => m.OverwrittenFileCount + 1), cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }

        // Keep a tracked copy consistent with the row it no longer matches (count and RowVersion).
        var tracked = dbContext.CustomModels.Local.FirstOrDefault(m => m.Id == file.CustomModelId);
        if (tracked is not null)
        {
            await dbContext.Entry(tracked).ReloadAsync(cancellationToken);
        }
    }

    public async Task<CustomModelRunSignal> GetRunSignalAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var row = await dbContext.CustomModels.AsNoTracking()
            .Where(m => m.Id == id)
            .Select(m => new { m.IsInProgress, m.CancellationRequestedAtUtc })
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null || !row.IsInProgress)
        {
            return CustomModelRunSignal.NoLongerInProgress;
        }

        return row.CancellationRequestedAtUtc is null ? CustomModelRunSignal.Continue : CustomModelRunSignal.CancellationRequested;
    }

    public async Task<IReadOnlyList<CustomModel>> ListInProgressAsync(CancellationToken cancellationToken = default) =>
        await dbContext.CustomModels.AsNoTracking().Where(m => m.IsInProgress).ToListAsync(cancellationToken);

    /// <summary>The model as the database has it now, discarding any stale tracked values. <see langword="null"/> when gone or removed.</summary>
    private async Task<CustomModel?> LoadCurrentAsync(Guid id, CancellationToken cancellationToken)
    {
        var tracked = dbContext.CustomModels.Local.FirstOrDefault(m => m.Id == id);
        if (tracked is null)
        {
            return await dbContext.CustomModels.FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
        }

        var entry = dbContext.Entry(tracked);
        await entry.ReloadAsync(cancellationToken);
        return entry.State == EntityState.Detached || tracked.IsDeleted ? null : tracked;
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (DuplicateMessage(ex) is { } message)
        {
            // Don't leave the rejected insert/update queued for the next save in this scope.
            foreach (var entry in ex.Entries)
            {
                entry.State = EntityState.Detached;
            }

            throw new DuplicateResourceException(message);
        }
    }

    private static string? DuplicateMessage(DbUpdateException ex)
    {
        if (ex.InnerException is not Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 } sqlException)
        {
            return null;
        }

        var message = sqlException.Message;
        if (message.Contains(CustomModelConfiguration.NameIndex, StringComparison.Ordinal))
        {
            return "A custom model with this name already exists. Choose a different name.";
        }

        if (message.Contains(CustomModelConfiguration.RepositoryAvailableIndex, StringComparison.Ordinal))
        {
            return "Another model from this repository is already available. Make it unavailable first.";
        }

        if (message.Contains(CustomModelConfiguration.DestinationInProgressIndex, StringComparison.Ordinal))
        {
            return "Another deployment to this destination is already in progress.";
        }

        return null;
    }
}
