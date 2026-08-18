using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace AskLucy.Web.Tests.Agents;

/// <summary>
/// spec.md FR-048/SC-010: an agent, its executions, and its history are visible/controllable only
/// by their owner. Mirrors <c>AskLucy.Web.Tests.Memory.MemoryCrossUserSecurityTests</c>'s scope
/// boundary for this environment: the cross-user 404-not-403 denial logic itself is unit-tested
/// at the Application layer (<c>AgentOwnershipGuard</c>/<c>AgentExecutionOwnershipGuard</c> via
/// the command/query handler tests), where it can be verified without a live database and a
/// second real authenticated user. This class covers every Agents/AgentExecutions endpoint's
/// outer auth gate (unauthenticated request → 401), which is what's runnable in this environment
/// without a seeded second user account.
/// </summary>
public sealed class AgentsControllerTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task CreateAgent_ShouldReturn401_WhenNoAuthorizationHeaderIsPresent()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/agents", new { name = "Hijacked" });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetAgent_ShouldReturn401_WhenNoAuthorizationHeaderIsPresent()
    {
        var response = await _client.GetAsync($"/api/v1/agents/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ListAgents_ShouldReturn401_WhenNoAuthorizationHeaderIsPresent()
    {
        var response = await _client.GetAsync("/api/v1/agents");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UpdateAgent_ShouldReturn401_WhenNoAuthorizationHeaderIsPresent()
    {
        var response = await _client.PutAsJsonAsync($"/api/v1/agents/{Guid.NewGuid()}", new { name = "Hijacked" });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PublishAgentVersion_ShouldReturn401_WhenNoAuthorizationHeaderIsPresent()
    {
        var response = await _client.PostAsJsonAsync($"/api/v1/agents/{Guid.NewGuid()}/versions", new { changeDescription = (string?)null });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task StartAgentExecution_ShouldReturn401_WhenNoAuthorizationHeaderIsPresent()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/v1/agent-executions",
            new { agentId = Guid.NewGuid(), objective = "Hijacked", conversationIntegrationMode = "Standalone" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetAgentExecution_ShouldReturn401_WhenNoAuthorizationHeaderIsPresent()
    {
        var response = await _client.GetAsync($"/api/v1/agent-executions/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
