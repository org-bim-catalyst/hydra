using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace AskLucy.Web.Tests.Auth;

/// <summary>
/// specs/058-password-recovery T039 (US3). Exercises the endpoint as a real signed-in caller, so
/// the acting session's survival (FR-010) is observed rather than assumed.
/// </summary>
public sealed class ChangePasswordEndpointTests(ForgotPasswordWebApplicationFactory factory)
    : IClassFixture<ForgotPasswordWebApplicationFactory>, IAsyncLifetime
{
    private const string Endpoint = "/api/v1/auth/change-password";
    private const string NewPassword = "Changed-Passw0rd!";

    private readonly List<string> _seededUserIds = [];

    private string _email = string.Empty;
    private string _userId = string.Empty;

    public async ValueTask InitializeAsync()
    {
        _email = $"change-{Guid.NewGuid():N}@example.com";
        _userId = await PasswordResetTestHelper.SeedUserAsync(factory.Services, _email);
        _seededUserIds.Add(_userId);
    }

    public ValueTask DisposeAsync() =>
        new(PasswordResetTestHelper.DeleteUsersAsync(factory.Services, _seededUserIds));

    /// <summary>Signs in and returns a client carrying both the bearer token and the refresh cookie.</summary>
    private async Task<HttpClient> SignInAsync(string password = PasswordResetTestHelper.SeedPassword)
    {
        // https, because the refresh cookie is Secure and the acting-session logic depends on it.
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { email = _email, password },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<LoginShape>(TestContext.Current.CancellationToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body!.AccessToken);

        return client;
    }

    private static Task<HttpResponseMessage> ChangeAsync(HttpClient client, string? currentPassword, string newPassword) =>
        client.PostAsJsonAsync(
            Endpoint,
            new { currentPassword, newPassword },
            TestContext.Current.CancellationToken);

    [Fact]
    public async Task ChangePassword_ShouldSucceed_AndLeaveTheActingSessionSignedIn()
    {
        var acting = await SignInAsync();
        var otherSession = await SignInAsync();

        (await ChangeAsync(acting, PasswordResetTestHelper.SeedPassword, NewPassword))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var actingRefresh = await acting.PostAsync("/api/v1/auth/refresh", content: null, TestContext.Current.CancellationToken);
        actingRefresh.StatusCode.Should().Be(HttpStatusCode.OK, "the session that changed the password stays signed in (FR-010)");

        var otherRefresh = await otherSession.PostAsync("/api/v1/auth/refresh", content: null, TestContext.Current.CancellationToken);
        otherRefresh.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "every other session is signed out (FR-010)");
    }

    [Fact]
    public async Task ChangePassword_ShouldReject_AWrongCurrentPassword()
    {
        var client = await SignInAsync();

        var response = await ChangeAsync(client, "not-my-password", NewPassword);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
            .Should().Contain("Current password is incorrect");
    }

    [Fact]
    public async Task ChangePassword_ShouldReject_AnOmittedCurrentPassword_WhenTheAccountHasOne()
    {
        var client = await SignInAsync();

        var response = await ChangeAsync(client, currentPassword: null, NewPassword);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
            .Should().Contain("Current password is required");
    }

    [Fact]
    public async Task PasswordStatus_ShouldReportThatThisAccountHasAPassword()
    {
        var client = await SignInAsync();

        var response = await client.GetFromJsonAsync<PasswordStatusShape>(
            "/api/v1/auth/password/status", TestContext.Current.CancellationToken);

        response!.HasPassword.Should().BeTrue();
    }

    private sealed record LoginShape(string? UserId, string? AccessToken, DateTime? ExpiresAtUtc, bool RequiresTwoFactor);

    private sealed record PasswordStatusShape(bool HasPassword);
}
