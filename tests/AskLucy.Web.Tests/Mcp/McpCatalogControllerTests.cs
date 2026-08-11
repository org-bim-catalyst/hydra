using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Xunit;

namespace AskLucy.Web.Tests.Mcp;

/// <summary>
/// specs/021-mcp-integration User Story 4 — the MCP tool catalog is reachable by any authenticated
/// user, unlike <see cref="McpServersControllerTests"/>'s admin-only surface; no role requirement,
/// just <c>[Authorize]</c> plus the dedicated `mcp-endpoints` rate-limit policy (T097).
/// </summary>
public sealed class McpCatalogControllerTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();
    private const string SomeNamespacedName = "mcp:3fa85f64-5717-4562-b3fc-2c963f66afa6:search";

    [Theory]
    [InlineData("/api/v1/mcp/catalog/tools")]
    [InlineData("/api/v1/mcp/catalog/tools/" + SomeNamespacedName)]
    public async Task ShouldReturn401_WhenAnonymous(string path)
    {
        var response = await _client.GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("/api/v1/mcp/catalog/tools")]
    [InlineData("/api/v1/mcp/catalog/tools/" + SomeNamespacedName)]
    public async Task ShouldPassAuthorization_ForAnyAuthenticatedUser_WithNoAdminRoleRequired(string path)
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.Create("user-1"));

        var response = await _client.GetAsync(path);

        // No live database in this environment (see CustomWebApplicationFactory) — proves
        // authorization let a plain, non-admin user through to the handler, never 401/403.
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }
}
