using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AskLucy.Domain.Mcp;
using AskLucy.Web.Contracts;
using FluentAssertions;
using Xunit;

namespace AskLucy.Web.Tests.Mcp;

/// <summary>
/// specs/021-mcp-integration User Story 1 — MCP server administration is gated by the same
/// <c>AdministratorOrSuperUser</c> authorization policy as every other admin controller. Same
/// no-live-database, self-signed-JWT pattern as <see cref="Agents.AgentPoliciesControllerTests"/> —
/// role evaluation is purely a function of the token's claims, independent of any data access.
/// Problem Details body shapes for <c>mcp-endpoint-not-allowed</c>/<c>mcp-server-endpoint-conflict</c>/
/// <c>mcp-server-has-references</c> require a live database to actually reach the validation logic
/// that produces them and are exercised by the Application-layer handler tests instead.
/// </summary>
public sealed class McpServersControllerTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();
    private static readonly Guid SomeServerId = Guid.NewGuid();
    private static readonly Guid SomeToolId = Guid.NewGuid();

    public static readonly TheoryData<string, string> Routes = new()
    {
        { "POST", "/api/v1/admin/mcp/servers" },
        { "GET", "/api/v1/admin/mcp/servers" },
        { "GET", "/api/v1/admin/mcp/servers/{id}" },
        { "PUT", "/api/v1/admin/mcp/servers/{id}" },
        { "DELETE", "/api/v1/admin/mcp/servers/{id}" },
        { "POST", "/api/v1/admin/mcp/servers/{id}/actions/enable" },
        { "POST", "/api/v1/admin/mcp/servers/{id}/actions/disable" },
        { "POST", "/api/v1/admin/mcp/servers/{id}/actions/test-connection" },
        { "POST", "/api/v1/admin/mcp/servers/{id}/actions/refresh-capabilities" },
        { "POST", "/api/v1/admin/mcp/servers/{id}/actions/rotate-credential" },
        { "GET", "/api/v1/admin/mcp/servers/{id}/health" },
        { "GET", "/api/v1/admin/mcp/servers/{id}/references" },
        { "GET", "/api/v1/admin/mcp/servers/{id}/tools" },
        { "GET", "/api/v1/admin/mcp/servers/{id}/audit-log" },
        { "POST", "/api/v1/admin/mcp/servers/{id}/tools/{toolId}/actions/activate" },
        { "POST", "/api/v1/admin/mcp/servers/{id}/tools/{toolId}/actions/deactivate" },
    };

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task ShouldReturn401_WhenAnonymous(string method, string pathTemplate)
    {
        var response = await SendAsync(method, pathTemplate);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task ShouldReturn403_WhenCallerHasNoAdminRole(string method, string pathTemplate)
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.Create("user-1"));

        var response = await SendAsync(method, pathTemplate);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [MemberData(nameof(Routes))]
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
        var path = pathTemplate.Replace("{id}", SomeServerId.ToString()).Replace("{toolId}", SomeToolId.ToString());

        return method switch
        {
            "GET" => _client.GetAsync(path),
            "POST" when path.EndsWith("/servers", StringComparison.Ordinal) =>
                _client.PostAsync(path, JsonContent.Create(new RegisterMcpServerRequest(
                    "Test", null, "https://mcp.example.com", McpServerTransport.StreamableHttp, McpAuthenticationType.ApiKey,
                    null, false, false, null, false, null, 60))),
            "POST" when path.EndsWith("/actions/activate", StringComparison.Ordinal) =>
                _client.PostAsync(path, JsonContent.Create(new ActivateMcpToolRequest(null, null))),
            "POST" when path.EndsWith("/actions/rotate-credential", StringComparison.Ordinal) =>
                _client.PostAsync(path, JsonContent.Create(new RotateMcpServerCredentialRequest("new-secret"))),
            "POST" => _client.PostAsync(path, content: null),
            "PUT" => _client.PutAsync(path, JsonContent.Create(new UpdateMcpServerRequest(
                "Test", null, "https://mcp.example.com", McpServerTransport.StreamableHttp, McpAuthenticationType.ApiKey,
                false, false, null, false, null, 60))),
            "DELETE" => _client.DeleteAsync(path),
            _ => throw new ArgumentOutOfRangeException(nameof(method)),
        };
    }
}
