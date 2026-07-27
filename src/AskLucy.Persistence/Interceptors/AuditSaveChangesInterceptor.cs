using AskLucy.Application.Abstractions;
using AskLucy.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AskLucy.Persistence.Interceptors;

/// <summary>
/// Populates <see cref="BaseEntity"/> audit columns on every save, per constitution &#167;5
/// ("populated by a SaveChanges interceptor, never set manually by callers"), and converts
/// hard deletes of <see cref="BaseEntity"/> rows into soft deletes.
/// </summary>
public sealed class AuditSaveChangesInterceptor(ICurrentUserAccessor currentUser) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Apply(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Apply(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Apply(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var actor = currentUser.UserId ?? "system";
        var now = DateTime.UtcNow;

        foreach (EntityEntry<BaseEntity> entry in context.ChangeTracker.Entries<BaseEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAtUtc = now;
                    entry.Entity.CreatedBy = actor;
                    break;
                case EntityState.Modified:
                    entry.Entity.ModifiedAtUtc = now;
                    entry.Entity.ModifiedBy = actor;
                    break;
                case EntityState.Deleted:
                    // Hard deletes are never allowed on audited aggregates — convert to soft delete.
                    entry.State = EntityState.Modified;
                    entry.Entity.DeletedAtUtc = now;
                    entry.Entity.DeletedBy = actor;
                    break;
            }
        }
    }
}
