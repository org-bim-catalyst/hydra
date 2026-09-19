using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Xunit;

namespace AskLucy.Web.Tests.Auth;

/// <summary>
/// specs/058-password-recovery T048 (US4, FR-014). An account created through an external provider
/// has no password to reset — the same emailed link has to be able to set its first one.
/// </summary>
public sealed class SetFirstPasswordTests(ForgotPasswordWebApplicationFactory factory)
    : IClassFixture<ForgotPasswordWebApplicationFactory>, IAsyncLifetime
{
    private const string FirstPassword = "First-Passw0rd-Ever!";

    private readonly HttpClient _client = factory.CreateClient();
    private readonly List<string> _seededUserIds = [];

    private string _email = string.Empty;
    private string _userId = string.Empty;

    public async ValueTask InitializeAsync()
    {
        _email = $"external-{Guid.NewGuid():N}@example.com";
        _userId = await PasswordResetTestHelper.SeedPasswordlessUserAsync(factory.Services, _email);
        _seededUserIds.Add(_userId);
    }

    public ValueTask DisposeAsync() =>
        new(PasswordResetTestHelper.DeleteUsersAsync(factory.Services, _seededUserIds));

    [Fact]
    public async Task RedeemingALink_ShouldSetAFirstPassword_OnAPasswordlessAccount()
    {
        var token = await PasswordResetTestHelper.IssueResetTokenAsync(factory.Services, _userId, _email);

        var reset = await _client.PostAsJsonAsync(
            "/api/v1/auth/password/reset",
            new { userId = _userId, token, newPassword = FirstPassword },
            TestContext.Current.CancellationToken);

        reset.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var signIn = await _client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { email = _email, password = FirstPassword },
            TestContext.Current.CancellationToken);

        signIn.StatusCode.Should().Be(HttpStatusCode.OK,
            "the account now has an email-and-password route in as well as its provider (FR-014)");
    }
}
