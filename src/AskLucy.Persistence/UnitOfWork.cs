using AskLucy.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence;

public sealed class UnitOfWork(AskLucyDbContext dbContext) : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        dbContext.SaveChangesAsync(cancellationToken);

    public async Task<bool> TrySaveChangesAsync(string uniqueIndexNameOnConflict, CancellationToken cancellationToken = default)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintConflict(ex, uniqueIndexNameOnConflict))
        {
            dbContext.ChangeTracker.Clear();
            return false;
        }
    }

    // Mirrors AskLucyDbContext.IsPromptVersionNumberConflict's own SQL Server error-number check
    // (2601/2627), generalised to any named unique index rather than one hardcoded to prompts.
    private static bool IsUniqueConstraintConflict(DbUpdateException ex, string indexName) =>
        ex.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 } sqlException &&
        sqlException.Message.Contains(indexName, StringComparison.Ordinal);
}
