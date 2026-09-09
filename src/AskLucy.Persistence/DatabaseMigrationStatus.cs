using AskLucy.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence;

/// <summary>specs/045 T104 — the same <see cref="Microsoft.EntityFrameworkCore.Migrations.IMigrator"/> query <see cref="HealthChecks.PendingMigrationsHealthCheck"/> already uses, exposed through the Application-layer abstraction so <c>Infrastructure</c> can check it without referencing <see cref="AskLucyDbContext"/> directly (constitution §3).</summary>
public sealed class DatabaseMigrationStatus(AskLucyDbContext dbContext) : IDatabaseMigrationStatus
{
    public async Task<bool> HasPendingMigrationsAsync(CancellationToken cancellationToken = default) =>
        (await dbContext.Database.GetPendingMigrationsAsync(cancellationToken)).Any();
}
