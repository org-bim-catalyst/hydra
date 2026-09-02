using System.Net;
using FluentAssertions;
using Xunit;

namespace AskLucy.Web.Tests.Workflows;

/// <summary>
/// spec.md User Story 8 (FR-051/FR-059) — every execution history endpoint's outer auth gate
/// (unauthenticated request → 401). Mirrors <c>AgentExecutionHistorySecurityTests</c>'s scope
/// boundary: cross-user 404-not-403 denial is unit-tested at the Application layer
/// (<c>WorkflowCrossUserSecurityTests</c>), where it's verifiable without a live database and a
/// second real authenticated user.
/// </summary>
public sealed class WorkflowExecutionHistorySecurityTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task ListWorkflowExecutions_ShouldReturn401_WhenNoAuthorizationHeaderIsPresent()
    {
        var response = await _client.GetAsync("/api/v1/workflow-executions", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetWorkflowExecutionNodes_ShouldReturn401_WhenNoAuthorizationHeaderIsPresent()
    {
        var response = await _client.GetAsync($"/api/v1/workflow-executions/{Guid.NewGuid()}/nodes", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetWorkflowExecutionUsage_ShouldReturn401_WhenNoAuthorizationHeaderIsPresent()
    {
        var response = await _client.GetAsync($"/api/v1/workflow-executions/{Guid.NewGuid()}/usage", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetWorkflowExecutionEvents_ShouldReturn401_WhenNoAuthorizationHeaderIsPresent()
    {
        var response = await _client.GetAsync($"/api/v1/workflow-executions/{Guid.NewGuid()}/events", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PauseWorkflowExecution_ShouldReturn401_WhenNoAuthorizationHeaderIsPresent()
    {
        var response = await _client.PostAsync($"/api/v1/workflow-executions/{Guid.NewGuid()}/pause", content: null, cancellationToken: TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ResumeWorkflowExecution_ShouldReturn401_WhenNoAuthorizationHeaderIsPresent()
    {
        var response = await _client.PostAsync($"/api/v1/workflow-executions/{Guid.NewGuid()}/resume", content: null, cancellationToken: TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CancelWorkflowExecution_ShouldReturn401_WhenNoAuthorizationHeaderIsPresent()
    {
        var response = await _client.PostAsync($"/api/v1/workflow-executions/{Guid.NewGuid()}/cancel", content: null, cancellationToken: TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetWorkflowStatistics_ShouldReturn401_WhenNoAuthorizationHeaderIsPresent()
    {
        var response = await _client.GetAsync("/api/v1/workflows/statistics", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
