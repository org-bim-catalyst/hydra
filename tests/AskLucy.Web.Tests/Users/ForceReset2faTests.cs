using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Xunit;

namespace AskLucy.Web.Tests.Users;

/// <summary>FR-015/FR-022 (specs/001-admin-dashboard).</summary>
public sealed class ForceReset2faTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task ShouldReturn403_WhenCallerHasNoAdminRole()
    {
        var token = TestJwtFactory.Create("user-1");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.PostAsync("/api/v1/users/some-other-user/actions/force-2fa-reset", content: null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ShouldReturn403_WhenTargetingSelf()
    {
        var token = TestJwtFactory.Create("admin-1", "Administrator");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.PostAsync("/api/v1/users/admin-1/actions/force-2fa-reset", content: null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ShouldPassAuthorization_WhenCallerHasAnAdminRoleAndTargetsSomeoneElse()
    {
        var token = TestJwtFactory.Create("admin-1", "Administrator");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.PostAsync("/api/v1/users/some-other-user/actions/force-2fa-reset", content: null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }
}
