using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace AskLucy.Web.Tests.Workflows;

/// <summary>
/// spec.md FR-059/SC-008: a workflow, its versions, and its executions are visible/controllable
/// only by their owner. Mirrors <c>AskLucy.Web.Tests.Agents.AgentsControllerTests</c>'s scope
/// boundary for this environment: the cross-user 404-not-403 denial logic itself is unit-tested
/// at the Application layer (<c>WorkflowOwnershipGuard</c>/<c>WorkflowExecutionOwnershipGuard</c>
/// via the command/query handler tests), where it can be verified without a live database and a
/// second real authenticated user. This class covers every Workflows/WorkflowExecutions endpoint's
/// outer auth gate (unauthenticated request → 401), which is what's runnable in this environment
/// without a seeded second user account.
/// </summary>
public sealed class WorkflowsControllerTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task CreateWorkflow_ShouldReturn401_WhenNoAuthorizationHeaderIsPresent()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/workflows", new { name = "Hijacked", workflowType = "Manual" }, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetWorkflow_ShouldReturn401_WhenNoAuthorizationHeaderIsPresent()
    {
        var response = await _client.GetAsync($"/api/v1/workflows/{Guid.NewGuid()}", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ListWorkflows_ShouldReturn401_WhenNoAuthorizationHeaderIsPresent()
    {
        var response = await _client.GetAsync("/api/v1/workflows", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UpdateWorkflow_ShouldReturn401_WhenNoAuthorizationHeaderIsPresent()
    {
        var response = await _client.PutAsJsonAsync(
            $"/api/v1/workflows/{Guid.NewGuid()}",
            new { name = "Hijacked", description = (string?)null, draftDefinitionJson = "{}" },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ValidateWorkflow_ShouldReturn401_WhenNoAuthorizationHeaderIsPresent()
    {
        var response = await _client.PostAsync($"/api/v1/workflows/{Guid.NewGuid()}/actions/validate", null, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PublishWorkflowVersion_ShouldReturn401_WhenNoAuthorizationHeaderIsPresent()
    {
        var response = await _client.PostAsJsonAsync(
            $"/api/v1/workflows/{Guid.NewGuid()}/versions",
            new { changeDescription = (string?)null },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetWorkflowVersion_ShouldReturn401_WhenNoAuthorizationHeaderIsPresent()
    {
        var response = await _client.GetAsync($"/api/v1/workflows/{Guid.NewGuid()}/versions/1", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ListWorkflowVersions_ShouldReturn401_WhenNoAuthorizationHeaderIsPresent()
    {
        var response = await _client.GetAsync($"/api/v1/workflows/{Guid.NewGuid()}/versions", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DuplicateWorkflow_ShouldReturn401_WhenNoAuthorizationHeaderIsPresent()
    {
        var response = await _client.PostAsync($"/api/v1/workflows/{Guid.NewGuid()}/actions/duplicate", null, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ArchiveWorkflow_ShouldReturn401_WhenNoAuthorizationHeaderIsPresent()
    {
        var response = await _client.PostAsync($"/api/v1/workflows/{Guid.NewGuid()}/actions/archive", null, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RestoreWorkflow_ShouldReturn401_WhenNoAuthorizationHeaderIsPresent()
    {
        var response = await _client.PostAsync($"/api/v1/workflows/{Guid.NewGuid()}/actions/restore", null, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DisableWorkflow_ShouldReturn401_WhenNoAuthorizationHeaderIsPresent()
    {
        var response = await _client.PostAsync($"/api/v1/workflows/{Guid.NewGuid()}/actions/disable", null, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task EnableWorkflow_ShouldReturn401_WhenNoAuthorizationHeaderIsPresent()
    {
        var response = await _client.PostAsync($"/api/v1/workflows/{Guid.NewGuid()}/actions/enable", null, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DeprecateWorkflow_ShouldReturn401_WhenNoAuthorizationHeaderIsPresent()
    {
        var response = await _client.PostAsync($"/api/v1/workflows/{Guid.NewGuid()}/actions/deprecate", null, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DeleteWorkflow_ShouldReturn401_WhenNoAuthorizationHeaderIsPresent()
    {
        var response = await _client.DeleteAsync($"/api/v1/workflows/{Guid.NewGuid()}", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task StartWorkflowExecution_ShouldReturn401_WhenNoAuthorizationHeaderIsPresent()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/v1/workflow-executions",
            new { workflowId = Guid.NewGuid(), workflowVersionNumber = (int?)null, inputsJson = "{}", triggerType = "Manual" },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetWorkflowExecution_ShouldReturn401_WhenNoAuthorizationHeaderIsPresent()
    {
        var response = await _client.GetAsync($"/api/v1/workflow-executions/{Guid.NewGuid()}", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetWorkflowApproval_ShouldReturn401_WhenNoAuthorizationHeaderIsPresent()
    {
        var response = await _client.GetAsync($"/api/v1/workflow-executions/{Guid.NewGuid()}/approvals/{Guid.NewGuid()}", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ApproveWorkflowNode_ShouldReturn401_WhenNoAuthorizationHeaderIsPresent()
    {
        var response = await _client.PostAsync($"/api/v1/workflow-executions/{Guid.NewGuid()}/approvals/{Guid.NewGuid()}/approve", null, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RejectWorkflowNode_ShouldReturn401_WhenNoAuthorizationHeaderIsPresent()
    {
        var response = await _client.PostAsJsonAsync($"/api/v1/workflow-executions/{Guid.NewGuid()}/approvals/{Guid.NewGuid()}/reject", new { reason = (string?)null }, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RequestWorkflowNodeChanges_ShouldReturn401_WhenNoAuthorizationHeaderIsPresent()
    {
        var response = await _client.PostAsJsonAsync($"/api/v1/workflow-executions/{Guid.NewGuid()}/approvals/{Guid.NewGuid()}/request-changes", new { comments = "Please revise." }, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
