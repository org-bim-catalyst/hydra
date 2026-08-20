using AskLucy.Persistence.HealthChecks;
using FluentAssertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AskLucy.Persistence.Tests.HealthChecks;

/// <summary>
/// specs/029-fix-chat-widget-bugs T006, FR-012, research.md Decision 2.
///
/// Only the Healthy path is covered against the real shared test database here — the
/// Unhealthy path (pending migrations present) would need a second database with no
/// <c>CREATE DATABASE</c> rights available in this shared-hosting test environment
/// (<see cref="PersistenceTestFixture"/>'s own doc comment), and <see cref="AskLucyDbContext"/>
/// is sealed with a concrete, non-interface <c>Database</c> facade, so it can't be
/// substituted for a unit test either. The check's own branching
/// (<c>pending.Count == 0</c>) is a single trivial condition on the result of EF Core's own,
/// separately-tested <c>GetPendingMigrationsAsync</c> API — the real integration risk this
/// test exists to catch (a genuine SQL round-trip against <c>__EFMigrationsHistory</c>
/// succeeding) is exercised by the Healthy case alone.
/// </summary>
[Collection(PersistenceTestCollection.Name)]
public sealed class PendingMigrationsHealthCheckTests(PersistenceTestFixture fixture)
{
    [Fact]
    public async Task CheckHealthAsync_ShouldReportHealthy_WhenNoMigrationsArePending()
    {
        await using var dbContext = fixture.CreateDbContext();
        var healthCheck = new PendingMigrationsHealthCheck(dbContext);

        var result = await healthCheck.CheckHealthAsync(
            new HealthCheckContext(), TestContext.Current.CancellationToken);

        result.Status.Should().Be(HealthStatus.Healthy);
    }
}
