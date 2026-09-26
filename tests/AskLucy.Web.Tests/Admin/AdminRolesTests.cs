using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace AskLucy.Web.Tests.Admin;

/// <summary>
/// specs/055-role-management User Story 1 — server-side authorization boundary for the Roles
/// screen's endpoints. Mirrors <see cref="RoleAuthorizationTests"/>'s no-live-database style:
/// a synthetic test-token subject id doesn't resolve to a real Identity row, so
/// <c>CurrentAuthorizationClaimsTransformation</c> deliberately leaves such a token's claims
/// untouched (see its own doc comment) and the token's baked-in role claim is what
/// <c>AdministratorOrSuperUser</c> evaluates — exactly as before that feature shipped. Full
/// CRUD/409/403-on-built-in behavior is covered by the mocked-repository handler tests in
/// AskLucy.Application.Tests.Authorization and by quickstart.md's live-database scenarios.
/// </summary>
public sealed class AdminRolesTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private static readonly string[] DashboardViewOnly = ["admin.dashboard.view"];

    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task GetRoles_ShouldReturn403_WhenCallerHasNoAdminRole()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.Create("user-1"));

        var response = await _client.GetAsync("/api/v1/admin/roles", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("Administrator")]
    [InlineData("Super User")]
    public async Task GetRoles_ShouldPassAuthorization_WhenCallerHasAnAdminRole(string role)
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.Create("admin-1", role));

        var response = await _client.GetAsync("/api/v1/admin/roles", TestContext.Current.CancellationToken);

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CreateRole_ShouldReturn403_WhenCallerHasNoAdminRole()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.Create("user-1"));

        var response = await _client.PostAsync(
            "/api/v1/admin/roles",
            JsonContent.Create(new { name = "Moderator", description = (string?)null, permissionKeys = DashboardViewOnly }),
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task UpdateRole_ShouldReturn403_WhenCallerHasNoAdminRole()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.Create("user-1"));

        var response = await _client.PutAsync(
            "/api/v1/admin/roles/some-role-id",
            JsonContent.Create(new { name = "Moderator", description = (string?)null, permissionKeys = DashboardViewOnly, concurrencyStamp = "stamp" }),
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DeleteRole_ShouldReturn403_WhenCallerHasNoAdminRole()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.Create("user-1"));

        var response = await _client.DeleteAsync(
            "/api/v1/admin/roles/some-role-id?concurrencyStamp=stamp", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task UpdateDefaultRole_ShouldReturn403_WhenCallerHasNoAdminRole()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.Create("user-1"));

        var response = await _client.PutAsync(
            "/api/v1/admin/roles/default",
            JsonContent.Create(new { description = (string?)null, permissionKeys = DashboardViewOnly, concurrencyStamp = "stamp" }),
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Administrator")]
    public async Task DuplicateRole_ShouldReturn403_WhenCallerIsNotASuperUser(string? role)
    {
        var token = role is null ? TestJwtFactory.Create("user-1") : TestJwtFactory.Create("admin-1", role);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.PostAsync(
            "/api/v1/admin/roles/some-role-id/duplicate",
            JsonContent.Create(new { name = "Copy of Moderator", description = (string?)null }),
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetRoles_ShouldReturn401_WhenUnauthenticated()
    {
        var response = await _client.GetAsync("/api/v1/admin/roles", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
