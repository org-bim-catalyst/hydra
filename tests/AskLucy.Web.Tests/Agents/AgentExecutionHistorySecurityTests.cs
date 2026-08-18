using System.Net;
using FluentAssertions;
using Xunit;

namespace AskLucy.Web.Tests.Agents;

/// <summary>
/// spec.md User Story 5 (FR-036/FR-048/FR-050) — every execution history endpoint's outer auth
/// gate (unauthenticated request → 401). Mirrors <c>AgentsControllerTests</c>'s scope boundary:
/// cross-user 404-not-403 denial is unit-tested at the Application layer
/// (<c>AgentCrossUserSecurityTests</c>), where it's verifiable without a live database and a
/// second real authenticated user.
/// </summary>
public sealed class AgentExecutionHistorySecurityTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task ListAgentExecutions_ShouldReturn401_WhenNoAuthorizationHeaderIsPresent()
    {
        var response = await _client.GetAsync("/api/v1/agent-executions");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetAgentExecutionSteps_ShouldReturn401_WhenNoAuthorizationHeaderIsPresent()
    {
        var response = await _client.GetAsync($"/api/v1/agent-executions/{Guid.NewGuid()}/steps");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetAgentToolCalls_ShouldReturn401_WhenNoAuthorizationHeaderIsPresent()
    {
        var response = await _client.GetAsync($"/api/v1/agent-executions/{Guid.NewGuid()}/tool-calls");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetAgentExecutionUsage_ShouldReturn401_WhenNoAuthorizationHeaderIsPresent()
    {
        var response = await _client.GetAsync($"/api/v1/agent-executions/{Guid.NewGuid()}/usage");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetAgentExecutionEvents_ShouldReturn401_WhenNoAuthorizationHeaderIsPresent()
    {
        var response = await _client.GetAsync($"/api/v1/agent-executions/{Guid.NewGuid()}/events");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PauseAgentExecution_ShouldReturn401_WhenNoAuthorizationHeaderIsPresent()
    {
        var response = await _client.PostAsync($"/api/v1/agent-executions/{Guid.NewGuid()}/pause", content: null);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ResumeAgentExecution_ShouldReturn401_WhenNoAuthorizationHeaderIsPresent()
    {
        var response = await _client.PostAsync($"/api/v1/agent-executions/{Guid.NewGuid()}/resume", content: null);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CancelAgentExecution_ShouldReturn401_WhenNoAuthorizationHeaderIsPresent()
    {
        var response = await _client.PostAsync($"/api/v1/agent-executions/{Guid.NewGuid()}/cancel", content: null);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
