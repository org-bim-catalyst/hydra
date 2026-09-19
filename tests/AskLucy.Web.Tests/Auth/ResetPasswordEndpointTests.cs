using System.Net;
using System.Net.Http.Json;
using AskLucy.Application.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AskLucy.Web.Tests.Auth;

/// <summary>
/// specs/058-password-recovery T025. The point of the suite is the sameness: reuse, expiry,
/// supersession and tampering must be indistinguishable from one another in the response (SC-006).
/// </summary>
public sealed class ResetPasswordEndpointTests(ForgotPasswordWebApplicationFactory factory)
    : IClassFixture<ForgotPasswordWebApplicationFactory>, IAsyncLifetime
{
    private const string Endpoint = "/api/v1/auth/password/reset";
    private const string NewPassword = "Redeemed-Passw0rd!";

    private readonly HttpClient _client = factory.CreateClient();
    private readonly List<string> _seededUserIds = [];

    private string _email = string.Empty;
    private string _userId = string.Empty;

    public async ValueTask InitializeAsync()
    {
        _email = $"reset-{Guid.NewGuid():N}@example.com";
        _userId = await PasswordResetTestHelper.SeedUserAsync(factory.Services, _email);
        _seededUserIds.Add(_userId);
    }

    public ValueTask DisposeAsync() =>
        new(PasswordResetTestHelper.DeleteUsersAsync(factory.Services, _seededUserIds));

    private Task<string> IssueTokenAsync(TimeSpan? lifetime = null) =>
        PasswordResetTestHelper.IssueResetTokenAsync(factory.Services, _userId, _email, lifetime);

    private Task<HttpResponseMessage> RedeemAsync(string token, string? newPassword = null) =>
        _client.PostAsJsonAsync(
            Endpoint,
            new { userId = _userId, token, newPassword = newPassword ?? NewPassword },
            TestContext.Current.CancellationToken);

    private Task<HttpResponseMessage> SignInAsync(string password) =>
        _client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new { email = _email, password },
            TestContext.Current.CancellationToken);

    private static async Task<string?> ReadTitleAsync(HttpResponseMessage response)
    {
        var problem = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>(
            TestContext.Current.CancellationToken);

        return problem?.TryGetValue("title", out var title) == true ? title.ToString() : null;
    }

    [Fact]
    public async Task ResetPassword_ShouldReplaceThePassword_SoTheOldOneNoLongerSignsIn()
    {
        var token = await IssueTokenAsync();

        (await RedeemAsync(token)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await SignInAsync(NewPassword)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await SignInAsync(PasswordResetTestHelper.SeedPassword)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ResetPassword_ShouldRejectEveryInvalidLink_WithOneIndistinguishableResponse()
    {
        var reusedToken = await IssueTokenAsync();
        (await RedeemAsync(reusedToken)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var expiredToken = await IssueTokenAsync(TimeSpan.FromHours(-1));

        var supersededToken = await IssueTokenAsync();
        await IssueTokenAsync(); // A newer link; on its own it does not supersede, so do it explicitly below.
        await SupersedeOutstandingLinksAsync();

        var tamperedToken = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");

        var responses = new List<HttpResponseMessage>
        {
            await RedeemAsync(reusedToken),
            await RedeemAsync(expiredToken),
            await RedeemAsync(supersededToken),
            await RedeemAsync(tamperedToken),
        };

        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.BadRequest);

        var titles = new List<string?>();
        foreach (var response in responses)
        {
            titles.Add(await ReadTitleAsync(response));
        }

        titles.Distinct().Should().ContainSingle(
            "reuse, expiry, supersession and tampering must be one undifferentiated failure (SC-006)");
    }

    private async Task SupersedeOutstandingLinksAsync()
    {
        using var scope = factory.Services.CreateScope();
        var tokens = scope.ServiceProvider.GetRequiredService<IPasswordResetTokenRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        await tokens.SupersedePendingForUserAsync(_userId, cancellationToken: CancellationToken.None);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ResetPassword_ShouldReportThePolicyFailures_AndLeaveTheLinkUsable()
    {
        var token = await IssueTokenAsync();

        var rejected = await RedeemAsync(token, "short");
        rejected.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Still redeemable: a weak first attempt must not cost the user their link.
        (await RedeemAsync(token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
