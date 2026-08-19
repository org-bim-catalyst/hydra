using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AskLucy.Web.Contracts;
using FluentAssertions;
using Xunit;

namespace AskLucy.Web.Tests.Workflows;

/// <summary>
/// spec.md User Story 5 ("Approval Policies") — administrator-managed auto-approval policy CRUD for
/// the workflow engine's platform-mandatory approval baseline is gated by the same
/// <c>AdministratorOrSuperUser</c> authorization policy as every other admin controller. Same
/// no-live-database, self-signed-JWT pattern as <see cref="Agents.AgentPoliciesControllerTests"/> —
/// role evaluation is purely a function of the token's claims, independent of any data access.
/// </summary>
public sealed class WorkflowPoliciesControllerTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();
    private static readonly Guid SomePolicyId = Guid.NewGuid();

    [Theory]
    [InlineData("GET", "/api/v1/admin/workflow-policies")]
    [InlineData("POST", "/api/v1/admin/workflow-policies")]
    [InlineData("PUT", "/api/v1/admin/workflow-policies/{id}")]
    [InlineData("DELETE", "/api/v1/admin/workflow-policies/{id}")]
    public async Task ShouldReturn401_WhenAnonymous(string method, string pathTemplate)
    {
        var response = await SendAsync(method, pathTemplate);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("GET", "/api/v1/admin/workflow-policies")]
    [InlineData("POST", "/api/v1/admin/workflow-policies")]
    [InlineData("PUT", "/api/v1/admin/workflow-policies/{id}")]
    [InlineData("DELETE", "/api/v1/admin/workflow-policies/{id}")]
    public async Task ShouldReturn403_WhenCallerHasNoAdminRole(string method, string pathTemplate)
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.Create("user-1"));

        var response = await SendAsync(method, pathTemplate);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("GET", "/api/v1/admin/workflow-policies")]
    [InlineData("POST", "/api/v1/admin/workflow-policies")]
    [InlineData("PUT", "/api/v1/admin/workflow-policies/{id}")]
    [InlineData("DELETE", "/api/v1/admin/workflow-policies/{id}")]
    public async Task ShouldPassAuthorization_WhenCallerIsAdministrator(string method, string pathTemplate)
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.Create("admin-1", "Administrator"));

        var response = await SendAsync(method, pathTemplate);

        // No live database in this environment (see CustomWebApplicationFactory) — proves
        // authorization let the admin through to the handler, never 401/403.
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    private Task<HttpResponseMessage> SendAsync(string method, string pathTemplate)
    {
        var path = pathTemplate.Replace("{id}", SomePolicyId.ToString());

        return method switch
        {
            "GET" => _client.GetAsync(path),
            "POST" => _client.PostAsync(path, JsonContent.Create(new CreateWorkflowPolicyRequest("Test policy", null, null, "FakeHighRiskTool", null))),
            "PUT" => _client.PutAsync(path, JsonContent.Create(new UpdateWorkflowPolicyRequest("Test policy", null, null, true))),
            "DELETE" => _client.DeleteAsync(path),
            _ => throw new ArgumentOutOfRangeException(nameof(method)),
        };
    }
}
