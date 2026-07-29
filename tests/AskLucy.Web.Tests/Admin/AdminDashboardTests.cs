using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Xunit;

namespace AskLucy.Web.Tests.Admin;

/// <summary>
/// FR-001/FR-018 (specs/001-admin-dashboard): the dashboard summary endpoint must be denied to
/// non-admins server-side and reachable by Administrator/Super User. Follows the same
/// self-signed-JWT, no-live-database pattern as <see cref="RoleAuthorizationTests"/> — role
/// evaluation is purely a function of the token's claims, independent of any data access.
/// </summary>
public sealed class AdminDashboardTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task GetSummary_ShouldReturn403_WhenCallerHasNoAdminRole()
    {
        var token = TestJwtFactory.Create("user-1");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.GetAsync("/api/v1/admin/dashboard/summary");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("Administrator")]
    [InlineData("Super User")]
    public async Task GetSummary_ShouldPassAuthorization_WhenCallerHasAnAdminRole(string role)
    {
        var token = TestJwtFactory.Create("admin-1", role);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.GetAsync("/api/v1/admin/dashboard/summary");

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetSummary_ShouldReturn401_WhenAnonymous()
    {
        var response = await _client.GetAsync("/api/v1/admin/dashboard/summary");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
