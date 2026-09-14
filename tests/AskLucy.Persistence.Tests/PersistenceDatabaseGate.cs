using System;

namespace AskLucy.Persistence.Tests;

/// <summary>
/// Opt-in gate for every test in this project: they run only against a database explicitly
/// declared disposable.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="PersistenceTestFixture"/> deletes every row from every EF-mapped table before the
/// suite runs, and the tests then insert their own users, chats, documents and memories. That is
/// correct against a throwaway database and destructive against anything else — and
/// <c>PERSISTENCE_TESTS_CONNECTION_STRING</c> pointed at the shared development database, which
/// wiped its migration-seeded reference rows (embedding providers, knowledge-base categories), its
/// AI provider configuration and every role assignment on each run, locally and in CI alike.
/// </para>
/// <para>
/// A connection string alone is therefore not enough to run: it says where a database is, not that
/// it may be emptied. Set <c>PERSISTENCE_TESTS_DEDICATED_DATABASE=1</c> only for a database that
/// exists purely for these tests. Without it every test here is reported as skipped with the
/// reason below, and the fixture never touches the database — see docs/TESTING.md §13.
/// </para>
/// </remarks>
public static class PersistenceDatabaseGate
{
    /// <summary>Environment variable that declares the target database disposable. Any value other than "1" keeps the gate shut.</summary>
    public const string EnvironmentVariable = "PERSISTENCE_TESTS_DEDICATED_DATABASE";

    public const string ConnectionStringEnvironmentVariable = "PERSISTENCE_TESTS_CONNECTION_STRING";

    public const string SkipReason =
        "Persistence tests empty and then write to their target database, so they run only against one "
        + "explicitly declared disposable. Set PERSISTENCE_TESTS_CONNECTION_STRING to a dedicated test "
        + "database and PERSISTENCE_TESTS_DEDICATED_DATABASE=1 — see PersistenceDatabaseGate and "
        + "docs/TESTING.md §13.";

    /// <summary>
    /// Read by xUnit through <c>[Fact(Skip = ..., SkipWhen = ..., SkipType = ...)]</c>, and by
    /// <see cref="PersistenceTestFixture.InitializeAsync"/> before it clears anything.
    /// </summary>
    public static bool NotConfigured =>
        Environment.GetEnvironmentVariable(EnvironmentVariable) != "1"
        || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable));
}
