using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AskLucy.Application.Abstractions;
using AskLucy.Persistence;
using AskLucy.Persistence.Identity;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Tests;

/// <summary>
/// Reversible but non-cryptographic <see cref="IMemoryContentProtector"/> for persistence
/// integration tests (specs/018-ai-memory-system) — this project references only
/// <c>AskLucy.Persistence</c>, not <c>AskLucy.Infrastructure</c> (where the real, Data
/// Protection-backed <c>MemoryContentProtector</c> lives), and these tests exercise SQL Server
/// round-tripping/query-filter/index behavior, not cryptographic correctness — that is covered
/// separately by an Infrastructure-level test of the real implementation.
/// </summary>
public sealed class Base64MemoryContentProtector : IMemoryContentProtector
{
    public string Protect(string plaintext) => Convert.ToBase64String(Encoding.UTF8.GetBytes(plaintext));

    public string Unprotect(string ciphertext) => Encoding.UTF8.GetString(Convert.FromBase64String(ciphertext));
}

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
            // tableName is a SQL identifier (schema-qualified table name) sourced solely from EF
            // Core's own compiled model metadata above, never from external/user input, and SQL
            // Server has no way to parameterize an identifier (ExecuteSqlAsync/
            // ExecuteSqlInterpolatedAsync parameterize data values, not identifiers, so switching
            // to them here would either throw or produce broken SQL) — safe to suppress.
#pragma warning disable EF1002
            await dbContext.Database.ExecuteSqlRawAsync($"ALTER TABLE {tableName} NOCHECK CONSTRAINT ALL");
#pragma warning restore EF1002
        }

        foreach (var tableName in tableNames)
        {
            // Same as above: tableName is an EF-model-derived SQL identifier, not a data value.
#pragma warning disable EF1002
            await dbContext.Database.ExecuteSqlRawAsync($"DELETE FROM {tableName}");
#pragma warning restore EF1002
        }

        // Deliberately "CHECK CONSTRAINT" (re-arm future enforcement), not "WITH CHECK CHECK
        // CONSTRAINT" (re-arm AND re-validate every existing row against the constraint right
        // now). Every table is already empty from the delete loop above, so there is nothing
        // meaningful to re-validate — and re-validating is exactly what failed in CI: SQL Server
        // scans a referencing table's current rows against the referenced table's current rows
        // as of that specific statement, table by table, so a table processed early in this loop
        // can be validated against a not-yet-fully-quiesced view of another table it references,
        // surfacing a spurious "conflicted with the FOREIGN KEY constraint" error even though
        // every table ends this method empty either way.
        foreach (var tableName in tableNames)
        {
            // Same as above: tableName is an EF-model-derived SQL identifier, not a data value.
#pragma warning disable EF1002
            await dbContext.Database.ExecuteSqlRawAsync($"ALTER TABLE {tableName} CHECK CONSTRAINT ALL");
#pragma warning restore EF1002
        }

        // Rebuild every full-text catalog after the data wipe. Because all tables are now empty
        // the rebuild is near-instant (nothing to re-index). Without this, the FTS change-tracking
        // log can accumulate a large pending-changes backlog across repeated INSERT→DELETE cycles
        // from successive test runs, which causes the async background population to fall behind
        // the 10-second SC-003 poll window and flake. The same catalogs were found broken on
        // 2026-08-18 and 2026-08-25 — both times fixed by a manual REBUILD against the shared
        // test DB; doing it here prevents the next occurrence without manual intervention.
        foreach (var catalog in new[] { "PromptSearchCatalog", "ConversationSearchCatalog", "RetrievalFullTextCatalog" })
        {
#pragma warning disable EF1002
            await dbContext.Database.ExecuteSqlRawAsync($"ALTER FULLTEXT CATALOG {catalog} REBUILD");
#pragma warning restore EF1002
        }
    }

    // CA1822 suggests making this static, since it only calls the (already-static)
    // ResolveConnectionString() helper and doesn't touch instance state. Deliberately left as an
    // instance method: every test in this project calls it as `fixture.CreateDbContext()` off the
    // xUnit collection-fixture instance injected into each test class's constructor (108 call
    // sites across every file in this project) — switching to static would force every one of
    // those call sites to `PersistenceTestFixture.CreateDbContext()` (CS0176 is a hard compiler
    // error, not a warning, for an instance-style call to a static member) purely to satisfy an
    // analyzer, with no behavioral benefit and a large, purely-mechanical diff across the whole
    // suite.
#pragma warning disable CA1822
    public AskLucyDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AskLucyDbContext>()
            .UseSqlServer(ResolveConnectionString())
            .Options;

        return new AskLucyDbContext(options, new Base64MemoryContentProtector());
    }
#pragma warning restore CA1822

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
