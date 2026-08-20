using System.Net;
using FluentAssertions;
using Xunit;

namespace AskLucy.Web.Tests.HealthChecks;

/// <summary>
/// specs/029-fix-chat-widget-bugs T007, contracts/health-readiness-endpoint.md — proves the
/// new endpoint is actually wired up end-to-end (registration, routing, and a real database
/// round-trip via <see cref="CustomWebApplicationFactory"/>'s shared test SQL Server
/// instance), on top of the check's own logic already covered by
/// AskLucy.Persistence.Tests's PendingMigrationsHealthCheckTests.
/// </summary>
public sealed class ReadinessHealthCheckTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task GetHealthReady_ShouldReturn200_WhenTheTestDatabaseIsFullyMigrated()
    {
        var response = await _client.GetAsync("/health/ready", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetHealth_ShouldStillWork_UnaffectedByTheNewReadyEndpoint()
    {
        var response = await _client.GetAsync("/health", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
