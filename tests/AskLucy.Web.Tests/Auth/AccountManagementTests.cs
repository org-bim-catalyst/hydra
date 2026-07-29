using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace AskLucy.Web.Tests.Auth;

/// <summary>
/// Covers the change-password, change-email, external-login-management, and
/// personal-data endpoints added alongside the account Settings page. Same tier as
/// <see cref="TwoFactorManagementTests"/> — authorization/validation boundaries only;
/// full success-path coverage needs a live database and is left to the Playwright
/// regression matrix (tests/AskLucy.E2E.Tests).
/// </summary>
public sealed class AccountManagementTests(CustomWebApplicationFactory factory)
    : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task ChangePassword_ShouldReturn401_WhenNoAuthorizationHeaderIsPresent()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/change-password", new { currentPassword = "x", newPassword = "y" });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RequestEmailChange_ShouldReturn401_WhenNoAuthorizationHeaderIsPresent()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/change-email/request", new { newEmail = "new@example.com" });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetExternalLogins_ShouldReturn401_WhenNoAuthorizationHeaderIsPresent()
    {
        var response = await _client.GetAsync("/api/v1/auth/external-logins");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RemoveExternalLogin_ShouldReturn401_WhenNoAuthorizationHeaderIsPresent()
    {
        var response = await _client.DeleteAsync("/api/v1/auth/external-logins/google/some-key");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DownloadPersonalData_ShouldReturn401_WhenNoAuthorizationHeaderIsPresent()
    {
        var response = await _client.GetAsync("/api/v1/users/me/personal-data");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DeleteAccount_ShouldReturn401_WhenNoAuthorizationHeaderIsPresent()
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, "/api/v1/users/me")
        {
            Content = JsonContent.Create(new { password = "x" }),
        };

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ConfirmEmail_ShouldReturn400_WhenTokenIsMissing()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/confirm-email", new { userId = "user-1", token = "" });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ConfirmEmailChange_ShouldReturn400_WhenNewEmailIsInvalid()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/v1/auth/change-email/confirm",
            new { userId = "user-1", newEmail = "not-an-email", token = "token" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
