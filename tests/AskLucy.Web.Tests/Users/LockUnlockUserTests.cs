using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Xunit;

namespace AskLucy.Web.Tests.Users;

/// <summary>FR-012/FR-013/FR-022 (specs/001-admin-dashboard). Same no-live-database pattern as <see cref="Admin.RoleAuthorizationTests"/>.</summary>
public sealed class LockUnlockUserTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Theory]
    [InlineData("/api/v1/users/some-other-user/actions/lock")]
    [InlineData("/api/v1/users/some-other-user/actions/unlock")]
    public async Task ShouldReturn403_WhenCallerHasNoAdminRole(string path)
    {
        var token = TestJwtFactory.Create("user-1");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.PostAsync(path, content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Lock_ShouldReturn403_WhenTargetingSelf()
    {
        var token = TestJwtFactory.Create("admin-1", "Administrator");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.PostAsync("/api/v1/users/admin-1/actions/lock", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("/api/v1/users/some-other-user/actions/lock")]
    [InlineData("/api/v1/users/some-other-user/actions/unlock")]
    public async Task ShouldPassAuthorization_WhenCallerHasAnAdminRoleAndTargetsSomeoneElse(string path)
    {
        var token = TestJwtFactory.Create("admin-1", "Administrator");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.PostAsync(path, content: null);

        // No live database in this environment (see CustomWebApplicationFactory) — proves
        // authorization let the admin through to the handler, never 401/403.
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }
}
