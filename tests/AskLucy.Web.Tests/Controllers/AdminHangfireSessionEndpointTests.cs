using System.Net;
using System.Net.Http.Headers;
using AskLucy.Web.Auth;
using FluentAssertions;
using Xunit;

namespace AskLucy.Web.Tests.Controllers;

/// <summary>
/// specs/060-hangfire-dashboard-access User Story 1 — the endpoint the "Jobs" sidebar entry
/// calls to mint the short-lived Hangfire-only cookie before opening <c>/hangfire</c>.
/// </summary>
public sealed class AdminHangfireSessionEndpointTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task IssueSession_ShouldReturn401_WhenUnauthenticated()
    {
        var response = await _client.PostAsync("/api/v1/admin/hangfire/session", content: null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task IssueSession_ShouldReturn403_WhenCallerHasNoAdminRole()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.Create("user-1"));

        var response = await _client.PostAsync("/api/v1/admin/hangfire/session", content: null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("Administrator")]
    [InlineData("Super User")]
    public async Task IssueSession_ShouldReturn204AndSetTheDashboardCookie_WhenCallerHasAnAdminRole(string role)
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestJwtFactory.Create("admin-1", role));

        var response = await _client.PostAsync("/api/v1/admin/hangfire/session", content: null, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        response.Headers.TryGetValues("Set-Cookie", out var setCookieHeaders).Should().BeTrue();
        var setCookie = setCookieHeaders!.Should().ContainSingle(h => h.StartsWith($"{HangfireDashboardCookie.Name}=", StringComparison.Ordinal)).Subject;

        setCookie.Should().Contain("path=/hangfire", "the cookie must never be sent to any other route");
        setCookie.Should().Contain("httponly", "the token must not be readable from client-side script");
        setCookie.Should().Contain("samesite=lax");
    }
}
