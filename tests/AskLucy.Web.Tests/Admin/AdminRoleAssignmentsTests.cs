using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace AskLucy.Web.Tests.Admin;

/// <summary>
/// specs/055-role-management User Story 2 — server-side authorization boundary for the Role
/// assignments screen's endpoints. See <see cref="AdminRolesTests"/>'s doc comment for why this
/// stays authorization-only (no live database in this environment).
/// </summary>
public sealed class AdminRoleAssignmentsTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task GetAssignments_ShouldReturn403_WhenCallerHasNoAdminRole()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.Create("user-1"));

        var response = await _client.GetAsync("/api/v1/admin/role-assignments", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("Administrator")]
    [InlineData("Super User")]
    public async Task GetAssignments_ShouldPassAuthorization_WhenCallerHasAnAdminRole(string role)
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.Create("admin-1", role));

        var response = await _client.GetAsync("/api/v1/admin/role-assignments", TestContext.Current.CancellationToken);

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AssignRole_ShouldReturn403_WhenCallerHasNoAdminRole()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.Create("user-1"));

        var response = await _client.PutAsync(
            "/api/v1/admin/role-assignments/some-user-id",
            JsonContent.Create(new { roleId = "some-role-id", expectedCurrentRoleId = (string?)null }),
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetAssignments_ShouldReturn401_WhenUnauthenticated()
    {
        var response = await _client.GetAsync("/api/v1/admin/role-assignments", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
