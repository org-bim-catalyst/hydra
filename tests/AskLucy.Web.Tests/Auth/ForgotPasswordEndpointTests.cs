using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using AskLucy.Application.Abstractions;
using AskLucy.Persistence.Identity;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace AskLucy.Web.Tests.Auth;

/// <summary>
/// Boots the host with the email job stubbed out, so exercising the endpoint neither talks to SMTP
/// nor leaves failing jobs in the shared Hangfire store.
/// </summary>
public sealed class ForgotPasswordWebApplicationFactory : CustomWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IPasswordEmailJob>();
            services.AddScoped<IPasswordEmailJob, NoOpPasswordEmailJob>();
        });
    }

    private sealed class NoOpPasswordEmailJob : IPasswordEmailJob
    {
        public Task SendResetLinkAsync(string userId, string email, string protectedToken, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task SendPasswordChangedNoticeAsync(string email, DateTime changedAtUtc, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}

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
        // The handler enqueues the email rather than awaiting SMTP precisely so this holds; if a
        // future change puts a send back on the request path, this test is what catches it.
        await PostAsync(_confirmedEmail); // Warm the pipeline so first-request JIT cost lands nowhere.

        var existing = await MeasureAsync(_confirmedEmail);
        var unknown = await MeasureAsync($"nobody-{Guid.NewGuid():N}@example.com");

        var slower = Math.Max(existing, unknown);
        var faster = Math.Min(existing, unknown);

        // Deliberately loose: this asserts no I/O-scale divergence (an SMTP round trip would be
        // hundreds of milliseconds), not a tight timing guarantee a shared CI host cannot honour.
        (slower - faster).Should().BeLessThan(250,
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
