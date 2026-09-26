using System;
using Microsoft.Data.SqlClient;

namespace AskLucy.Persistence.Tests;

/// <summary>
/// Gate for every test in this project: they run only against the database set aside for them.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="PersistenceTestFixture"/> deletes every row from every EF-mapped table before the
/// suite runs, and the tests then insert their own users, chats, documents and memories. That is
/// correct against a throwaway database and destructive against anything else. When this suite
/// shared <c>PERSISTENCE_TESTS_CONNECTION_STRING</c> with Web.Tests, it pointed at the shared
/// development database, and each run wiped its migration-seeded reference rows (embedding
/// providers, knowledge-base categories), its AI provider configuration and every role assignment.
/// </para>
/// <para>
/// The suite therefore reads its own variable, <c>PERSISTENCE_TESTS_2_CONNECTION_STRING</c>, which
/// names the dedicated test2 database and nothing else (a repository secret of the same name in
/// CI). Without it every test here is reported as skipped with the reason below, and the fixture
/// never touches a database. If it names the same database as
/// <c>PERSISTENCE_TESTS_CONNECTION_STRING</c>, <see cref="ResolveConnectionString"/> throws before
/// the fixture wipes anything — see docs/TESTING.md §13.
/// </para>
/// </remarks>
public static class PersistenceDatabaseGate
{
    /// <summary>The dedicated database for this suite. It is emptied on every run.</summary>
    public const string ConnectionStringEnvironmentVariable = "PERSISTENCE_TESTS_2_CONNECTION_STRING";

    /// <summary>The shared test database Web.Tests uses. This suite must never point at it.</summary>
    public const string SharedConnectionStringEnvironmentVariable = "PERSISTENCE_TESTS_CONNECTION_STRING";

    public const string SkipReason =
        "Persistence tests empty and then write to their target database, so they run only against the "
        + "one set aside for them. Set PERSISTENCE_TESTS_2_CONNECTION_STRING to the dedicated test2 "
        + "database — see PersistenceDatabaseGate and docs/TESTING.md §13.";

    /// <summary>
    /// Read by xUnit through <c>[Fact(Skip = ..., SkipWhen = ..., SkipType = ...)]</c>, and by
    /// <see cref="PersistenceTestFixture.InitializeAsync"/> before it clears anything.
    /// </summary>
    public static bool NotConfigured =>
        string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable));

    /// <summary>
    /// The dedicated connection string, refused when it names the shared test database: the same
    /// server and database under a different spelling, password or option order is still that
    /// database, so the comparison is on those two parts rather than the raw string.
    /// </summary>
    public static string ResolveConnectionString()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(SkipReason);
        }

        var shared = Environment.GetEnvironmentVariable(SharedConnectionStringEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(shared) && SameDatabase(connectionString, shared))
        {
            throw new InvalidOperationException(
                $"{ConnectionStringEnvironmentVariable} names the same database as "
                + $"{SharedConnectionStringEnvironmentVariable}, the shared test database Web.Tests uses. "
                + "Persistence tests empty their database, so they refuse to run against it — point "
                + $"{ConnectionStringEnvironmentVariable} at the dedicated test2 database (docs/TESTING.md §13).");
        }

        return connectionString;
    }

    private static bool SameDatabase(string first, string second)
    {
        var a = new SqlConnectionStringBuilder(first);
        var b = new SqlConnectionStringBuilder(second);

        return string.Equals(a.DataSource.Trim(), b.DataSource.Trim(), StringComparison.OrdinalIgnoreCase)
            && string.Equals(a.InitialCatalog.Trim(), b.InitialCatalog.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
