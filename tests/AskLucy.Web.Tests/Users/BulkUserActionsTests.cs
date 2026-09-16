using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AskLucy.Web.Contracts;
using AskLucy.Web.Tests;
using FluentAssertions;
using Xunit;

namespace AskLucy.Web.Tests.Users;

/// <summary>
/// specs/056-bulk-select-all: the same server-side authorization boundary as the existing
/// single-row Users actions (RoleAuthorizationTests) applies identically to every new bulk
/// endpoint — no separate/weaker authorization surface for "bulk" versions of an action.
/// No live database in this environment (matches the existing no-live-DB pattern), so a
/// passing-authorization request cannot succeed end-to-end; what's verified is that
/// authorization itself never blocks an admin (401/403) the way it blocks a non-admin.
/// </summary>
public sealed class BulkUserActionsTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private static readonly BulkTargetRequest ValidTarget = new(["user-1"], false);

    [Theory]
    [InlineData("actions/bulk-lock")]
    [InlineData("actions/bulk-unlock")]
    [InlineData("actions/bulk-force-2fa-reset")]
    public async Task BulkPostAction_ShouldReturn403_WhenCallerHasNoAdminPermission(string action)
    {
        var token = TestJwtFactory.Create("user-1");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.PostAsJsonAsync($"/api/v1/users/{action}", ValidTarget, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("actions/bulk-lock")]
    [InlineData("actions/bulk-unlock")]
    [InlineData("actions/bulk-force-2fa-reset")]
    public async Task BulkPostAction_ShouldPassAuthorization_WhenCallerHasAnAdminRole(string action)
    {
        var token = TestJwtFactory.Create("admin-1", "Administrator");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.PostAsJsonAsync($"/api/v1/users/{action}", ValidTarget, TestContext.Current.CancellationToken);

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task BulkDelete_ShouldReturn403_WhenCallerHasNoAdminPermission()
    {
        var token = TestJwtFactory.Create("user-1");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var request = new HttpRequestMessage(HttpMethod.Delete, "/api/v1/users/actions/bulk-delete")
        {
            Content = JsonContent.Create(ValidTarget),
        };
        var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task BulkDelete_ShouldPassAuthorization_WhenCallerHasAnAdminRole()
    {
        var token = TestJwtFactory.Create("admin-1", "Super User");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var request = new HttpRequestMessage(HttpMethod.Delete, "/api/v1/users/actions/bulk-delete")
        {
            Content = JsonContent.Create(ValidTarget),
        };
        var response = await _client.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetBulkEligibleIds_ShouldReturn403_WhenCallerHasNoAdminPermission()
    {
        var token = TestJwtFactory.Create("user-1");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.GetAsync(
            "/api/v1/users/actions/bulk-eligible-ids?action=Lock", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetBulkEligibleIds_ShouldPassAuthorization_WhenCallerHasAnAdminRole()
    {
        var token = TestJwtFactory.Create("admin-1", "Administrator");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.GetAsync(
            "/api/v1/users/actions/bulk-eligible-ids?action=Lock", TestContext.Current.CancellationToken);

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }
}
