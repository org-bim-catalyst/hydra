using System;
using System.Linq;
using System.Threading.Tasks;
using AskLucy.Persistence;
using AskLucy.Persistence.Identity;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Tests;

/// <summary>
/// Connects to a real, dedicated test SQL Server instance (constitution &#167;10: integration
/// tests run against a real/test SQL Server instance, not a fake provider) and resets it to a
/// clean schema on every run, so tests exercise the actual global query filters/indexes/
/// constraints EF Core generates — not an in-memory approximation of them.
///
/// Previously used Testcontainers to spin up a throwaway Linux SQL Server container per run;
/// removed because the backend CI job runs on <c>windows-latest</c>, whose Docker daemon
/// cannot run that (Linux-only) image — every run failed with
/// <c>DockerImageNotFoundException</c>, on CI and on Windows dev machines alike. This instance
/// is a persistent, shared test database instead, so <see cref="InitializeAsync"/> deletes
/// every row from every table (schema untouched) to guarantee the same fresh-data guarantee
/// Testcontainers gave, rather than migrating/creating the database itself: this account is a
/// shared-hosting (site4now.net) database user scoped to this one already-provisioned
/// database, with no rights to touch <c>master</c> at all. Both
/// <see cref="RelationalDatabaseFacadeExtensions.EnsureDeletedAsync"/> and, surprisingly,
/// <see cref="RelationalDatabaseFacadeExtensions.MigrateAsync(Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade)"/>
/// itself were tried first — both fail here with "CREATE DATABASE permission denied in
/// database 'master'", because EF Core's migration pipeline always runs a database-exists
/// check against <c>master</c> before applying migrations, regardless of whether the database
/// already has every migration applied. Schema changes for this database are therefore applied
/// separately via the `dotnet ef database update` CLI (which hits the same check, but is run
/// deliberately by a maintainer, not implicitly on every test run) — see docs/TESTING.md §13.
/// Because the database is shared rather than per-run, CI serializes the job that uses it (see
/// the `concurrency` group on `backend-build-and-test` in ci.yml) so two runs never reset/query
/// it at the same time.
/// </summary>
public sealed class PersistenceTestFixture : IAsyncLifetime
{
    private const string ConnectionStringEnvVar = "PERSISTENCE_TESTS_CONNECTION_STRING";

    private static string ResolveConnectionString()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionStringEnvVar);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Persistence tests need a real test SQL Server instance. Set the " +
                $"{ConnectionStringEnvVar} environment variable to its connection string " +
                $"(dotnet user-secrets or a local .env for development; a GitHub Actions " +
                $"secret in CI) — see docs/TESTING.md §13.");
        }

        return connectionString;
    }

    public async ValueTask InitializeAsync()
    {
        await using var dbContext = CreateDbContext();

        // Clears every EF-mapped table's data (schema untouched) so each run starts from the
        // same empty-tables state a fresh Testcontainers instance gave, without ever touching
        // the database itself.
        //
        // Previously used `sp_MSforeachtable`. Replaced for two reasons found while adding
        // specs/015's DocumentStatistics table (which has a filtered index):
        //  1. `sp_MSforeachtable`/`sp_MSforeach_worker` are system procedures whose own
        //     QUOTED_IDENTIFIER setting is fixed at the compile time of the SQL Server instance,
        //     not inherited from the caller's connection — the DELETE it runs internally fails
        //     against any table with a filtered index/computed column/indexed view (here:
        //     DocumentStatistics, and even AspNetUsers/AspNetRoles's own filtered unique
        //     indexes) with "DELETE failed because the following SET options have incorrect
        //     settings: 'QUOTED_IDENTIFIER'" — reproduced identically via both sqlcmd and the
        //     real Microsoft.Data.SqlClient connection this fixture uses. Building each
        //     statement in C# and executing it directly runs under this connection's own
        //     (correct) SET options, sidestepping the problem entirely.
        //  2. `sp_MSforeachtable` enumerates every user table in the database indiscriminately,
        //     including `__EFMigrationsHistory` — wiping the maintainer's migration-history
        //     record for this shared database on every test run, not just domain data. Deriving
        //     the table list from the EF Core model instead means `__EFMigrationsHistory` (which
        //     isn't part of the model) is never touched, by construction rather than by an
        //     exclusion filter.
        var tableNames = dbContext.Model.GetEntityTypes()
            .Select(entityType => entityType.GetSchemaQualifiedTableName())
            .Where(name => name is not null)
            .Distinct()
            .ToList();

        foreach (var tableName in tableNames)
        {
            await dbContext.Database.ExecuteSqlRawAsync($"ALTER TABLE {tableName} NOCHECK CONSTRAINT ALL");
        }

        foreach (var tableName in tableNames)
        {
            await dbContext.Database.ExecuteSqlRawAsync($"DELETE FROM {tableName}");
        }

        foreach (var tableName in tableNames)
        {
            await dbContext.Database.ExecuteSqlRawAsync($"ALTER TABLE {tableName} WITH CHECK CHECK CONSTRAINT ALL");
        }
    }

    public AskLucyDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AskLucyDbContext>()
            .UseSqlServer(ResolveConnectionString())
            .Options;

        return new AskLucyDbContext(options);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    /// <summary>
    /// Builds the <see cref="ApplicationUser"/> row a test's fabricated owner/user id must have
    /// before it can be used as a foreign key: <c>UserChats.UserId</c>/<c>Messages</c> enforce a
    /// real FK to <c>AspNetUsers</c> (<see cref="AskLucy.Persistence.Configurations.UserChatConfiguration"/>),
    /// which the in-memory provider never checked but this real SQL Server instance does.
    /// </summary>
    public static ApplicationUser CreateTestUser(string userId) => new()
    {
        Id = userId,
        UserName = userId,
        NormalizedUserName = userId.ToUpperInvariant(),
        Email = $"{userId}@persistence.tests.local",
        NormalizedEmail = $"{userId}@persistence.tests.local".ToUpperInvariant(),
        EmailConfirmed = true,
        CreatedAtUtc = DateTime.UtcNow,
    };
}

// CA1711 flags the "Collection" suffix, but this is xUnit's own required naming pattern
// for a collection-fixture definition, not an ambiguous System.Collections-style name.
#pragma warning disable CA1711
[CollectionDefinition(Name)]
public sealed class PersistenceTestCollection : ICollectionFixture<PersistenceTestFixture>
{
    public const string Name = "Persistence";
}
#pragma warning restore CA1711
