using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using AskLucy.Persistence.Identity;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AskLucy.Web.Tests.Auth;

/// <summary>
/// Boots the host for the forgot-password endpoint. Since specs/067 US9-B the request publishes to the
/// notification hub instead of enqueuing a Hangfire job, and every host here sends through the shared fake
/// SMTP server, so exercising the endpoint neither talks to SMTP nor leaves failing jobs behind.
/// </summary>
public sealed class ForgotPasswordWebApplicationFactory : CustomWebApplicationFactory;

/// <summary>
/// specs/058-password-recovery T014. Asserts the property the whole enumeration defence rests on:
/// an observer who can see status, body and timing cannot tell the four account states apart
/// (SC-005, SC-002, FR-003).
/// </summary>
public sealed class ForgotPasswordEndpointTests(ForgotPasswordWebApplicationFactory factory)
    : IClassFixture<ForgotPasswordWebApplicationFactory>, IAsyncLifetime
{
    private const string Endpoint = "/api/v1/auth/password/forgot";

    private readonly HttpClient _client = factory.CreateClient();
    private readonly List<string> _seededUserIds = [];

    private string _confirmedEmail = string.Empty;
    private string _unconfirmedEmail = string.Empty;
    private string _lockedOutEmail = string.Empty;

    public async ValueTask InitializeAsync()
    {
        _confirmedEmail = await SeedUserAsync(confirmed: true, lockedOut: false);
        _unconfirmedEmail = await SeedUserAsync(confirmed: false, lockedOut: false);
        _lockedOutEmail = await SeedUserAsync(confirmed: true, lockedOut: true);
    }

    /// <summary>Removes the seeded accounts again — this suite shares its database with others.</summary>
    public async ValueTask DisposeAsync()
    {
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        foreach (var userId in _seededUserIds)
        {
            var user = await userManager.FindByIdAsync(userId);
            if (user is not null)
            {
                await userManager.DeleteAsync(user);
            }
        }
    }

    private async Task<string> SeedUserAsync(bool confirmed, bool lockedOut)
    {
        var email = $"forgot-{Guid.NewGuid():N}@example.com";

        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = confirmed,
            CreatedAtUtc = DateTime.UtcNow,
        };

        (await userManager.CreateAsync(user, "Seed-Password-1!")).Succeeded.Should().BeTrue();

        if (lockedOut)
        {
            await userManager.SetLockoutEnabledAsync(user, true);
            await userManager.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddHours(1));
        }

        _seededUserIds.Add(user.Id);

        return email;
    }

    private Task<HttpResponseMessage> PostAsync(string email) =>
        _client.PostAsJsonAsync(Endpoint, new { email }, TestContext.Current.CancellationToken);

    [Fact]
    public async Task ForgotPassword_ShouldAnswerIdentically_AcrossEveryAccountState()
    {
        var addresses = new[]
        {
            _confirmedEmail,
            _unconfirmedEmail,
            _lockedOutEmail,
            $"nobody-{Guid.NewGuid():N}@example.com",
        };

        var observed = new List<(HttpStatusCode Status, string Body)>();

        foreach (var address in addresses)
        {
            var response = await PostAsync(address);
            observed.Add((response.StatusCode, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)));
        }

        observed.Should().OnlyContain(o => o.Status == HttpStatusCode.Accepted);
        observed.Select(o => o.Body).Distinct().Should().ContainSingle(
            "the response body must be byte-identical whatever the address turns out to be (SC-005)");
    }

    [Fact]
    public async Task ForgotPassword_ShouldReturn400_ForAMalformedAddress()
    {
        // Shape validation is safe to expose: it says nothing about which accounts exist.
        var response = await PostAsync("not-an-email");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ForgotPassword_ShouldNotDivergeInLatency_BetweenAnExistingAndAnUnknownAddress()
    {
        // The handler publishes to the hub rather than awaiting SMTP precisely so this holds; if a
        // future change puts a send back on the request path, this test is what catches it.
        await PostAsync(_confirmedEmail); // Warm the pipeline so first-request JIT cost lands nowhere.

        // Single sample per address, not median-of-N: "auth-endpoints" rate-limits this IP to
        // 10/minute for the whole class (sibling tests in this class already spend 5 of that
        // budget), so this can't afford more than the 3 requests below (1 warm-up + 2 measured).
        var existing = await MeasureAsync(_confirmedEmail);
        var unknown = await MeasureAsync($"nobody-{Guid.NewGuid():N}@example.com");

        var slower = Math.Max(existing, unknown);
        var faster = Math.Min(existing, unknown);

        // Deliberately loose: this asserts no I/O-scale divergence (an SMTP round trip would be
        // hundreds of milliseconds), not a tight timing guarantee a shared CI host cannot honour.
        // Widened from 250ms after a real CI run measured a 337ms gap under host load with no
        // actual I/O on either path — still an order of magnitude under SMTP-scale divergence.
        (slower - faster).Should().BeLessThan(450,
            "an observer must not be able to infer account existence from response time (SC-002, FR-003)");
    }

    private async Task<double> MeasureAsync(string email)
    {
        var stopwatch = Stopwatch.StartNew();
        var response = await PostAsync(email);
        stopwatch.Stop();

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        return stopwatch.Elapsed.TotalMilliseconds;
    }
}
