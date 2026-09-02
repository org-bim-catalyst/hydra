using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace AskLucy.Web.Tests.Auth;

/// <summary>
/// FR-011 (T030/G3): the 2FA enrollment/disable/recovery-code management endpoints
/// require authentication, same as every other authenticated endpoint. Full
/// success-path coverage (enroll → verify → disable) requires a live database with a
/// seeded user and is exercised by the Playwright regression matrix
/// (tests/AskLucy.E2E.Tests) instead, not this environment's unit/contract test tier.
/// </summary>
public sealed class TwoFactorManagementTests(CustomWebApplicationFactory factory)
    : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Enable_ShouldReturn401_WhenNoAuthorizationHeaderIsPresent()
    {
        var response = await _client.PostAsync("/api/v1/auth/2fa/enable", null, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Disable_ShouldReturn401_WhenNoAuthorizationHeaderIsPresent()
    {
        var response = await _client.PostAsync("/api/v1/auth/2fa/disable", null, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RecoveryCodes_ShouldReturn401_WhenNoAuthorizationHeaderIsPresent()
    {
        var response = await _client.PostAsync("/api/v1/auth/2fa/recovery-codes", null, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_ShouldReturn400_WhenEmailIsMissing()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email = "", password = "x" }, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
