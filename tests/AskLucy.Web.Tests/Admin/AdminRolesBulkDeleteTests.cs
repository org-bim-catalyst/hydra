using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AskLucy.Web.Contracts;
using FluentAssertions;
using Xunit;

namespace AskLucy.Web.Tests.Admin;

/// <summary>specs/056-bulk-select-all — same authorization boundary as the existing Roles endpoints (RoleAuthorizationTests-equivalent).</summary>
public sealed class AdminRolesBulkDeleteTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task BulkDelete_ShouldReturn403_WhenCallerHasNoAdminRole()
    {
        var token = TestJwtFactory.Create("user-1");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.PostAsJsonAsync(
            "/api/v1/admin/roles/actions/bulk-delete", new BulkTargetRequest(["role-1"], false), TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("Administrator")]
    [InlineData("Super User")]
    public async Task BulkDelete_ShouldPassAuthorization_WhenCallerHasAnAdminRole(string role)
    {
        var token = TestJwtFactory.Create("admin-1", role);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.PostAsJsonAsync(
            "/api/v1/admin/roles/actions/bulk-delete", new BulkTargetRequest(["role-1"], false), TestContext.Current.CancellationToken);

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetBulkEligibleIds_ShouldReturn403_WhenCallerHasNoAdminRole()
    {
        var token = TestJwtFactory.Create("user-1");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.GetAsync(
            "/api/v1/admin/roles/actions/bulk-eligible-ids", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
