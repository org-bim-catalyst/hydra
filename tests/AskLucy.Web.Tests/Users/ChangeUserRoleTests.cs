using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace AskLucy.Web.Tests.Users;

/// <summary>FR-014 privilege-escalation guard (specs/001-admin-dashboard, Clarifications 2026-07-28).</summary>
public sealed class ChangeUserRoleTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task ShouldReturn403_WhenCallerHasNoAdminRole()
    {
        var token = TestJwtFactory.Create("user-1");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.PatchAsync("/api/v1/users/some-other-user/role", JsonContent.Create(new { role = "Regular" }), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ShouldReturn403_WhenPlainAdministratorAttemptsToGrantSuperUser()
    {
        var token = TestJwtFactory.Create("admin-1", "Administrator");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.PatchAsync("/api/v1/users/some-other-user/role", JsonContent.Create(new { role = "Super User" }), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ShouldReturn403_WhenPlainAdministratorAttemptsToGrantAdministrator()
    {
        var token = TestJwtFactory.Create("admin-1", "Administrator");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.PatchAsync("/api/v1/users/some-other-user/role", JsonContent.Create(new { role = "Administrator" }), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ShouldPassAuthorization_WhenPlainAdministratorChangesARegularUsersRoleToRegular()
    {
        var token = TestJwtFactory.Create("admin-1", "Administrator");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.PatchAsync("/api/v1/users/some-other-user/role", JsonContent.Create(new { role = "Regular" }), TestContext.Current.CancellationToken);

        // No live database in this environment — proves the plain-Administrator privilege
        // guard did NOT reject this request (never 401/403), since neither the caller's
        // request nor the target's current role is privileged in this scenario.
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ShouldPassAuthorization_WhenSuperUserGrantsSuperUser()
    {
        var token = TestJwtFactory.Create("super-1", "Super User");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.PatchAsync("/api/v1/users/some-other-user/role", JsonContent.Create(new { role = "Super User" }), TestContext.Current.CancellationToken);

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }
}
