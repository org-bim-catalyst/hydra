using System.Net;
using System.Net.Http.Json;
using AskLucy.Web.Auth;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace AskLucy.Web.Tests.Auth;

/// <summary>
/// specs/058-password-recovery T026 (SC-007, FR-009). Two independent sign-ins produce two refresh
/// token families; completing a reset must end both, including the one that redeemed the link.
/// </summary>
public sealed class PasswordChangeSessionRevocationTests(ForgotPasswordWebApplicationFactory factory)
    : IClassFixture<ForgotPasswordWebApplicationFactory>, IAsyncLifetime
{
    private const string NewPassword = "Revoked-Everywhere-1!";

    private readonly List<string> _seededUserIds = [];

    private string _email = string.Empty;
    private string _userId = string.Empty;

    public async ValueTask InitializeAsync()
    {
        _email = $"revoke-{Guid.NewGuid():N}@example.com";
        _userId = await PasswordResetTestHelper.SeedUserAsync(factory.Services, _email);
        _seededUserIds.Add(_userId);
    }

    public ValueTask DisposeAsync() =>
        new(PasswordResetTestHelper.DeleteUsersAsync(factory.Services, _seededUserIds));

    /// <summary>
    /// The refresh cookie is <c>Secure</c>, so the test client has to speak https or the handler
    /// silently drops it and every refresh looks like an expired session.
    /// </summary>
    private HttpClient CreateHttpsClient() =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

    /// <summary>One client per session: the refresh cookie is what distinguishes the two families.</summary>
    private async Task<HttpClient> SignInAsync()
    {
        var client = CreateHttpsClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { email = _email, password = PasswordResetTestHelper.SeedPassword },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.GetValues("Set-Cookie").Should().Contain(c => c.StartsWith(RefreshTokenCookie.Name, StringComparison.Ordinal));

        return client;
    }

    [Fact]
    public async Task CompletingAReset_ShouldRevokeEveryRefreshTokenFamily()
    {
        var firstSession = await SignInAsync();
        var secondSession = await SignInAsync();

        // Both sessions are live before the reset — otherwise the assertion below proves nothing.
        (await RefreshAsync(firstSession)).Should().Be(HttpStatusCode.OK);
        (await RefreshAsync(secondSession)).Should().Be(HttpStatusCode.OK);

        var token = await PasswordResetTestHelper.IssueResetTokenAsync(factory.Services, _userId, _email);

        var reset = await CreateHttpsClient().PostAsJsonAsync(
            "/api/v1/auth/password/reset",
            new { userId = _userId, token, newPassword = NewPassword },
            TestContext.Current.CancellationToken);

        reset.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await RefreshAsync(firstSession)).Should().Be(HttpStatusCode.Unauthorized);
        (await RefreshAsync(secondSession)).Should().Be(HttpStatusCode.Unauthorized);
    }

    private static async Task<HttpStatusCode> RefreshAsync(HttpClient client)
    {
        var response = await client.PostAsync(
            "/api/v1/auth/refresh", content: null, TestContext.Current.CancellationToken);

        return response.StatusCode;
    }
}
