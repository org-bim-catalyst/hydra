using AskLucy.Application.Abstractions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence;

public sealed class UnitOfWork(AskLucyDbContext dbContext) : IUnitOfWork
{
    // Business transactions here always commit through exactly one SaveChanges call
    // (constitution §3/§15 — no multi-step partial commits), so a genuine parent-row
    // concurrency conflict and a same-batch new-child insert always land in the same
    // SaveChangesAsync. EF Core doesn't guarantee an existing, concurrency-checked parent's
    // UPDATE executes before an unrelated new child's INSERT within that batch (there's no
    // generated-key dependency forcing that order) — so a stale read can surface as a raw
    // unique-index-violation DbUpdateException instead of DbUpdateConcurrencyException,
    // observed via Prompt.ApplyEdit's PromptVersions insert racing its own Prompts row
    // update. The only way IX_PromptVersions_PromptId_VersionNumber can be violated at all
    // is exactly this: a concurrent request already advanced CurrentVersionNumber past what
    // this request read, so re-throwing as DbUpdateConcurrencyException here is a correct
    // translation, not a guess — ProblemDetailsMiddleware then reports the intended 409.
    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsPromptVersionNumberConflict(ex))
        {
            throw new DbUpdateConcurrencyException(
                "The prompt was modified by another request before this edit's new version could be saved.", ex);
        }
    }

    private static bool IsPromptVersionNumberConflict(DbUpdateException ex) =>
        ex.InnerException is SqlException { Number: 2601 or 2627 } sqlException &&
        sqlException.Message.Contains("IX_PromptVersions_PromptId_VersionNumber", StringComparison.Ordinal);
}
