using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Xunit;

namespace AskLucy.Web.Tests.Admin;

/// <summary>
/// FR-017 (T058, User Story 4): Control Panel / user-management actions must be
/// enforced server-side by role, not just hidden in the UI (the legacy behavior). Uses
/// a self-signed test JWT so the authorization *policy* (role check) can be verified
/// without a live database — unlike the endpoints' own data access, role evaluation is
/// entirely a function of the token's claims.
/// </summary>
public sealed class RoleAuthorizationTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task GetAllUsers_ShouldReturn403_WhenCallerHasNoAdminRole()
    {
        var token = TestJwtFactory.Create("user-1");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.GetAsync("/api/v1/users", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("Administrator")]
    [InlineData("Super User")]
    public async Task GetAllUsers_ShouldPassAuthorization_WhenCallerHasAnAdminRole(string role)
    {
        var token = TestJwtFactory.Create("admin-1", role);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.GetAsync("/api/v1/users", TestContext.Current.CancellationToken);

        // No live database in this environment, so the request cannot succeed end-to-end —
        // what this proves is that authorization let the admin through to the handler
        // (never 401/403), unlike the no-role case above.
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task UpdateUser_ShouldReturn403_WhenCallerHasNoAdminRole()
    {
        var token = TestJwtFactory.Create("user-1");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.PatchAsync(
            "/api/v1/users/some-other-user",
            System.Net.Http.Json.JsonContent.Create(new { firstName = "Eve" }),
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
