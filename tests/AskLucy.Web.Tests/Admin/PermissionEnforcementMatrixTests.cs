using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Xunit;

namespace AskLucy.Web.Tests.Admin;

/// <summary>
/// research.md Decision 5: for each catalogue-area endpoint, a synthetic principal holding only
/// that area's View permission can read but not write, and a principal holding none of that
/// area's permissions is rejected on both. No live database needed — these are pure
/// authorization-boundary checks (see <see cref="RoleAuthorizationTests"/>'s own doc comment).
/// </summary>
public sealed class PermissionEnforcementMatrixTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    public static TheoryData<string, string, string, string> ViewGatedReads() => new()
    {
        { "GET", "/api/v1/admin/dashboard/summary", "admin.dashboard.view", "custom" },
        { "GET", "/api/v1/users", "admin.users.view", "custom" },
        { "GET", "/api/v1/admin/ai/providers", "admin.ai-providers.view", "custom" },
        { "GET", "/api/v1/admin/ai/capabilities", "admin.ai-capabilities.view", "custom" },
        { "GET", "/api/v1/admin/ai/capabilities/settings", "admin.ai-capabilities.view", "custom" },
        { "GET", "/api/v1/admin/agent-policies", "admin.agent-policies.view", "custom" },
        { "GET", "/api/v1/admin/agents/system", "admin.system-agents.view", "custom" },
        { "GET", "/api/v1/admin/workflow-policies", "admin.workflow-policies.view", "custom" },
        { "GET", "/api/v1/admin/mcp/servers", "admin.mcp-servers.view", "custom" },
    };

    [Theory]
    [MemberData(nameof(ViewGatedReads))]
    public async Task ViewOnlyPrincipal_CanReadThisArea(string method, string path, string viewPermission, string userId)
    {
        var token = TestJwtFactory.Create(userId, [], [viewPermission]);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path), TestContext.Current.CancellationToken);

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    [Theory]
    [MemberData(nameof(ViewGatedReads))]
    public async Task PrincipalWithNoPermissions_CannotReadThisArea(string method, string path, string viewPermission, string userId)
    {
        _ = viewPermission;
        var token = TestJwtFactory.Create(userId, [], []);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    public static TheoryData<string, string, string> ManageGatedWrites() => new()
    {
        { "PATCH", "/api/v1/users/some-user-id", "admin.users.view" },
        { "PATCH", "/api/v1/admin/ai/providers/00000000-0000-0000-0000-000000000001", "admin.ai-providers.view" },
        { "PUT", "/api/v1/admin/ai/capabilities/BoundaryVision/settings", "admin.ai-capabilities.view" },
        { "POST", "/api/v1/admin/agent-policies", "admin.agent-policies.view" },
        { "POST", "/api/v1/admin/workflow-policies", "admin.workflow-policies.view" },
        { "POST", "/api/v1/admin/mcp/servers", "admin.mcp-servers.view" },
    };

    [Theory]
    [MemberData(nameof(ManageGatedWrites))]
    public async Task ViewOnlyPrincipal_CannotWriteToThisArea(string method, string path, string viewPermission)
    {
        var token = TestJwtFactory.Create("view-only-user", [], [viewPermission]);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
