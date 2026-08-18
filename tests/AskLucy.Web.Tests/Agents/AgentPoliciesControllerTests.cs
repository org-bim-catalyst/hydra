using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AskLucy.Web.Contracts;
using FluentAssertions;
using Xunit;

namespace AskLucy.Web.Tests.Agents;

/// <summary>
/// spec.md User Story 3 (FR-025/FR-026, research.md Decision 1) — administrator-managed
/// auto-approval policy CRUD is gated by the same <c>AdministratorOrSuperUser</c> authorization
/// policy as every other admin controller. Same no-live-database, self-signed-JWT pattern as
/// <see cref="Ai.AdminAiProvidersControllerTests"/> — role evaluation is purely a function of the
/// token's claims, independent of any data access.
/// </summary>
public sealed class AgentPoliciesControllerTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();
    private static readonly Guid SomePolicyId = Guid.NewGuid();

    [Theory]
    [InlineData("GET", "/api/v1/admin/agent-policies")]
    [InlineData("POST", "/api/v1/admin/agent-policies")]
    [InlineData("PUT", "/api/v1/admin/agent-policies/{id}")]
    [InlineData("DELETE", "/api/v1/admin/agent-policies/{id}")]
    public async Task ShouldReturn401_WhenAnonymous(string method, string pathTemplate)
    {
        var response = await SendAsync(method, pathTemplate);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("GET", "/api/v1/admin/agent-policies")]
    [InlineData("POST", "/api/v1/admin/agent-policies")]
    [InlineData("PUT", "/api/v1/admin/agent-policies/{id}")]
    [InlineData("DELETE", "/api/v1/admin/agent-policies/{id}")]
    public async Task ShouldReturn403_WhenCallerHasNoAdminRole(string method, string pathTemplate)
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.Create("user-1"));

        var response = await SendAsync(method, pathTemplate);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("GET", "/api/v1/admin/agent-policies")]
    [InlineData("POST", "/api/v1/admin/agent-policies")]
    [InlineData("PUT", "/api/v1/admin/agent-policies/{id}")]
    [InlineData("DELETE", "/api/v1/admin/agent-policies/{id}")]
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
            "POST" => _client.PostAsync(path, JsonContent.Create(new CreateAgentPolicyRequest("Test policy", null, "FakeHighRiskTool", null))),
            "PUT" => _client.PutAsync(path, JsonContent.Create(new UpdateAgentPolicyRequest("Test policy", null, null, true))),
            "DELETE" => _client.DeleteAsync(path),
            _ => throw new ArgumentOutOfRangeException(nameof(method)),
        };
    }
}
