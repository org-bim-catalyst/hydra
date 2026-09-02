using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Xunit;

namespace AskLucy.Web.Tests.Documents;

/// <summary>
/// T116 (US6 AC6, FR-045a) — the organization-wide dashboard is admin-only; the per-user
/// dashboard is reachable by any authenticated caller. Same self-signed-JWT technique as
/// <c>RoleAuthorizationTests</c> — authorization is a pure function of the token's claims, so it
/// is verifiable without a live database.
/// </summary>
public sealed class DashboardAuthorizationTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task GetOrganizationDashboard_ShouldReturn403_WhenCallerHasNoAdminRole()
    {
        var token = TestJwtFactory.Create("user-1");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.GetAsync("/api/v1/documents/dashboard/organization", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("Administrator")]
    [InlineData("Super User")]
    public async Task GetOrganizationDashboard_ShouldPassAuthorization_WhenCallerHasAnAdminRole(string role)
    {
        var token = TestJwtFactory.Create("admin-1", role);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.GetAsync("/api/v1/documents/dashboard/organization", TestContext.Current.CancellationToken);

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetDashboard_ShouldBeReachable_ByAnyAuthenticatedCaller_WithNoAdminRole()
    {
        var token = TestJwtFactory.Create("user-1");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.GetAsync("/api/v1/documents/dashboard", TestContext.Current.CancellationToken);

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }
}
