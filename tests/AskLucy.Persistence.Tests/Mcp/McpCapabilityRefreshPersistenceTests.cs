using AskLucy.Domain.Mcp;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Tests.Mcp;

/// <summary>
/// The uniqueness contract of <c>NamespacedName</c> for <see cref="McpTool"/> and
/// <see cref="McpResource"/> against a real SQL Server instance.
/// <para>
/// Both entities derive their namespaced name deterministically from the owning server and the
/// item's own name/URI, and both carry an unfiltered unique index on it. A capability refresh
/// therefore has to re-point the existing row at the new snapshot rather than insert a second one
/// for the same item — an insert-per-snapshot refresh throws a duplicate-key error on its second
/// run and leaves that server's capabilities permanently stale. The handler tests cannot see this:
/// they substitute the repository, so the index is never exercised. This suite can.
/// </para>
/// </summary>
[Collection(PersistenceTestCollection.Name)]
public sealed class McpCapabilityRefreshPersistenceTests(PersistenceTestFixture fixture)
{
    private const string AdminId = "admin-mcp";

    private static McpServer RegisterServer() => McpServer.Register(
        "Test", null, "https://mcp.example.com", McpServerTransport.StreamableHttp,
        McpAuthenticationType.ApiKey, false, false, null, false, null, AdminId, 60);

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task TwoSnapshots_CannotBothHoldAToolWithTheSameNamespacedName()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var server = RegisterServer();

        await using var dbContext = fixture.CreateDbContext();
        dbContext.McpServers.Add(server);
        var first = McpCapabilitySnapshot.CreateSuccessful(server.Id, 1, "[]", null, AdminId);
        var second = McpCapabilitySnapshot.CreateSuccessful(server.Id, 2, "[]", null, AdminId);
        dbContext.McpCapabilitySnapshots.AddRange(first, second);

        dbContext.McpTools.Add(McpTool.CreateFromDiscovery(
            server.Id, first.Id, "search", "Search", "Searches things.", "{}", "{}", null, null, "[]", null));
        await dbContext.SaveChangesAsync(cancellationToken);

        dbContext.McpTools.Add(McpTool.CreateFromDiscovery(
            server.Id, second.Id, "search", "Search", "Searches things.", "{}", "{}", null, null, "[]", null));

        var act = async () => await dbContext.SaveChangesAsync(cancellationToken);

        await act.Should().ThrowAsync<DbUpdateException>(
            "NamespacedName is unique per server+tool, so a refresh that inserts a second row for the " +
            "same tool collides with the row it is replacing");
    }

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task RefreshFromSnapshot_ShouldUpdateTheExistingRowInPlace()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var server = RegisterServer();
        Guid toolId;
        Guid secondSnapshotId;

        await using (var seed = fixture.CreateDbContext())
        {
            seed.McpServers.Add(server);
            var first = McpCapabilitySnapshot.CreateSuccessful(server.Id, 1, "[]", null, AdminId);
            var second = McpCapabilitySnapshot.CreateSuccessful(server.Id, 2, "[]", null, AdminId);
            secondSnapshotId = second.Id;
            seed.McpCapabilitySnapshots.AddRange(first, second);

            var tool = McpTool.CreateFromDiscovery(
                server.Id, first.Id, "search", "Search", "Searches things.", "{}", "{}", null, null, "[]", null);
            toolId = tool.Id;
            seed.McpTools.Add(tool);
            await seed.SaveChangesAsync(cancellationToken);
        }

        await using (var refresh = fixture.CreateDbContext())
        {
            var tool = await refresh.McpTools.SingleAsync(t => t.Id == toolId, cancellationToken);
            tool.RefreshFromSnapshot(secondSnapshotId, "Search v2", "Searches things, v2.", "{\"x\":1}", "{}", null, "2", definitionChanged: false, AdminId);
            await refresh.SaveChangesAsync(cancellationToken);
        }

        await using var read = fixture.CreateDbContext();
        var tools = await read.McpTools.Where(t => t.McpServerId == server.Id).ToListAsync(cancellationToken);

        tools.Should().ContainSingle("the refresh mutates the row instead of adding a second one");
        tools[0].McpCapabilitySnapshotId.Should().Be(secondSnapshotId);
        tools[0].DisplayName.Should().Be("Search v2");
        tools[0].Version.Should().Be("2");
    }

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task TwoSnapshots_CannotBothHoldAResourceWithTheSameNamespacedName()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var server = RegisterServer();

        await using var dbContext = fixture.CreateDbContext();
        dbContext.McpServers.Add(server);
        var first = McpCapabilitySnapshot.CreateSuccessful(server.Id, 1, "[]", null, AdminId);
        var second = McpCapabilitySnapshot.CreateSuccessful(server.Id, 2, "[]", null, AdminId);
        dbContext.McpCapabilitySnapshots.AddRange(first, second);

        dbContext.McpResources.Add(McpResource.CreateFromDiscovery(
            server.Id, first.Id, "file:///notes.txt", "Notes", null, "text/plain"));
        await dbContext.SaveChangesAsync(cancellationToken);

        dbContext.McpResources.Add(McpResource.CreateFromDiscovery(
            server.Id, second.Id, "file:///notes.txt", "Notes", null, "text/plain"));

        var act = async () => await dbContext.SaveChangesAsync(cancellationToken);

        await act.Should().ThrowAsync<DbUpdateException>(
            "McpResources.NamespacedName is unique per server+URI for the same reason McpTools.NamespacedName is");
    }
}
