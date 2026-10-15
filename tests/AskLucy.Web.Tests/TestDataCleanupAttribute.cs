using System.Reflection;
using Microsoft.Data.SqlClient;
using Xunit.v3;

[assembly: AskLucy.Web.Tests.TestDataCleanupAttribute]

namespace AskLucy.Web.Tests;

/// <summary>
/// Deletes the rows a database test created, once that test has ended. It applies to every test in
/// <see cref="DatabaseSerialGroup"/>. Only rows stamped inside the test's own time window are removed, and only from the tables the
/// tests write to, child tables first. Identity tables are excluded on purpose: the host seeds built-in roles and a dev admin at
/// startup, and those rows can land inside a test's window. Seed data and the migration history are never touched.
/// </summary>
public sealed class TestDataCleanupAttribute : BeforeAfterTestAttribute
{
    private static readonly string[] Tables =
    [
        "dbo.NotificationAuditLogs",
        "dbo.NotificationDeliveries",
        "dbo.NotificationOutboxEvents",
        "dbo.Notifications",
        "dbo.RoleAuditLogs",
        "dbo.KnowledgeBaseAuditLogs",
        "dbo.OperationalFailureOccurrences",
        "dbo.DocumentChecksums",
        "dbo.DocumentStatistics",
        "dbo.RefreshTokens",
    ];

    // Everything written since the previous cleanup is removed, so rows written in class setup, between tests, are covered too.
    private static DateTime _lastCleanupUtc = DateTime.UtcNow;

    public override void After(MethodInfo methodUnderTest, IXunitTest test)
    {
        // An exception here fails the test that just ran, so a cleanup problem is never silently skipped.
        if (!IsDatabaseTest(test))
        {
            return;
        }

        var now = DateTime.UtcNow;
        DeleteRowsSince(_lastCleanupUtc).GetAwaiter().GetResult();
        _lastCleanupUtc = now;
    }

    private static async Task DeleteRowsSince(DateTime windowStart)
    {
        var connectionString = Environment.GetEnvironmentVariable("PERSISTENCE_TESTS_CONNECTION_STRING")
            ?? throw new InvalidOperationException("PERSISTENCE_TESTS_CONNECTION_STRING is not set, so test-data cleanup cannot run.");

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();

        foreach (var table in Tables)
        {
            // The table names are constants above, so the statement text is never built from input.
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"DELETE FROM {table} WHERE CreatedAtUtc >= @windowStart";
            command.Parameters.AddWithValue("@windowStart", windowStart);
            await command.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
    }

    private static bool IsDatabaseTest(IXunitTest test) =>
        test.TestCase.TestMethod.TestClass.TestCollection.CollectionDefinition == typeof(DatabaseSerialGroup);
}
