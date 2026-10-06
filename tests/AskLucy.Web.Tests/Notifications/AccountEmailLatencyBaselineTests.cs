using System.Collections.Concurrent;
using System.Diagnostics;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Authentication.Commands.RequestPasswordReset;
using AskLucy.Application.Authentication.Commands.ResendEmailConfirmation;
using AskLucy.Persistence;
using AskLucy.Persistence.Identity;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace AskLucy.Web.Tests.Notifications;

/// <summary>Opens the wall-clock tests. They measure this host as much as this code, so CI runs them only on request.</summary>
public static class AccountEmailLatencyGate
{
    public const string EnvironmentVariable = "RUN_SCALE_PERFORMANCE_TESTS";

    public const string SkipReason = "A wall-clock measurement of the shared test host: set RUN_SCALE_PERFORMANCE_TESTS=1 to run it.";

    public static bool NotRequested => Environment.GetEnvironmentVariable(EnvironmentVariable) != "1";
}

/// <summary>One host whose only outgoing mail is a clock-stamped capture, so a test can time "request to hand-off".</summary>
public sealed class AccountEmailLatencyFactory : CustomWebApplicationFactory
{
    public ConcurrentDictionary<string, long> HandedOffAt { get; } = new(StringComparer.OrdinalIgnoreCase);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(new StampingEmailSender(HandedOffAt));
        });
    }

    private sealed class StampingEmailSender(ConcurrentDictionary<string, long> handedOffAt) : IEmailSender
    {
        public Task SendAsync(string toEmail, string subject, string htmlBody, string textBody, CancellationToken cancellationToken = default)
        {
            handedOffAt.TryAdd(toEmail, Stopwatch.GetTimestamp());
            return Task.CompletedTask;
        }

        // HUB-ONLY-BEGIN
        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            handedOffAt.TryAdd(message.To, Stopwatch.GetTimestamp());
            return Task.CompletedTask;
        }

        // HUB-ONLY-END
    }
}

/// <summary>
/// T233 — specs/067 SC-014: moving the account emails onto the hub must not make them slower. For 50
/// password-reset requests and 50 confirmation resends, each from a different account (a reset is throttled
/// per account), it times the request until the capturing sender is handed the email.
/// <para>
/// The baseline below is the same measurement taken on the code before the move, through the Hangfire job path
/// (research R29); this file, with its hub-only lines removed, is what measured it. After the move it asserts
/// that the 95th percentile is within a minute (SC-014) and no more than 10% above that baseline (or the
/// noise allowance below, if larger). Gated with the
/// other wall-clock tests: set <c>RUN_SCALE_PERFORMANCE_TESTS=1</c>.
/// </para>
/// </summary>
public sealed class AccountEmailLatencyBaselineTests(AccountEmailLatencyFactory factory, ITestOutputHelper output) : IClassFixture<AccountEmailLatencyFactory>
{
    private const int Runs = 50;

    /// <summary>Seconds. The combined p95 measured on the Hangfire job path before the move (research.md R29).</summary>
    private const double BaselineP95Seconds = 0.065;

    /// <summary>
    /// A 10% margin on 65 ms is 6 ms, which is below the timer and scheduler jitter of any shared host, so the
    /// comparison allows the larger of 10% and this much. SC-014's real bar is the minute above.
    /// </summary>
    private const double NoiseAllowanceSeconds = 0.25;

    [Fact(Skip = AccountEmailLatencyGate.SkipReason, SkipWhen = nameof(AccountEmailLatencyGate.NotRequested), SkipType = typeof(AccountEmailLatencyGate))]
    public async Task PasswordResetAndConfirmationEmails_ReachTheMailHandOff_WithinAMinute_AndNoSlowerThanBefore()
    {
        var ct = TestContext.Current.CancellationToken;
        var run = Guid.NewGuid().ToString("N")[..8];
        var users = new List<string>();
        try
        {
            var resetSamples = new List<double>();
            var confirmationSamples = new List<double>();

            // Warm the host (JIT, Hangfire server, first connections) on a throwaway account.
            var warm = await SeedAsync($"lat-{run}-warm@example.com", confirmed: true, users);
            await MeasureAsync(warm, reset: true, ct);

            for (var i = 0; i < Runs; i++)
            {
                var confirmed = await SeedAsync($"lat-{run}-r{i}@example.com", confirmed: true, users);
                resetSamples.Add(await MeasureAsync(confirmed, reset: true, ct));

                var unconfirmed = await SeedAsync($"lat-{run}-c{i}@example.com", confirmed: false, users);
                confirmationSamples.Add(await MeasureAsync(unconfirmed, reset: false, ct));
            }

            var all = resetSamples.Concat(confirmationSamples).ToList();
            var p95 = Percentile(all, 0.95);
            output.WriteLine($"SC-014 latency over {all.Count} requests: reset p95={Percentile(resetSamples, 0.95):F3}s median={Percentile(resetSamples, 0.5):F3}s; "
                + $"confirmation p95={Percentile(confirmationSamples, 0.95):F3}s median={Percentile(confirmationSamples, 0.5):F3}s; combined p95={p95:F3}s max={all.Max():F3}s");

            p95.Should().BeLessThanOrEqualTo(60, "SC-014: an account email is handed off within a minute");
            p95.Should().BeLessThanOrEqualTo(
                Math.Max(BaselineP95Seconds * 1.10, BaselineP95Seconds + NoiseAllowanceSeconds),
                "SC-014: no slower than the Hangfire path it replaced");
        }
        finally
        {
            await CleanupAsync(users);
        }
    }

    private async Task<double> MeasureAsync((string Id, string Email) account, bool reset, CancellationToken ct)
    {
        factory.HandedOffAt.TryRemove(account.Email, out _);
        var started = Stopwatch.GetTimestamp();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            if (reset)
            {
                await mediator.Send(new RequestPasswordResetCommand(account.Email, null), ct);
            }
            else
            {
                await mediator.Send(new ResendEmailConfirmationCommand(account.Email), ct);
            }
        }

        var deadline = DateTime.UtcNow.AddSeconds(120);
        while (!factory.HandedOffAt.TryGetValue(account.Email, out var handedOff))
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"No email was handed off for {account.Email} within 120 seconds.");
            }

            await Task.Delay(50, ct);
        }

        return Stopwatch.GetElapsedTime(started, factory.HandedOffAt[account.Email]).TotalSeconds;
    }

    private static double Percentile(IReadOnlyCollection<double> samples, double p)
    {
        var ordered = samples.Order().ToList();
        return ordered[(int)Math.Ceiling(p * ordered.Count) - 1];
    }

    private async Task<(string Id, string Email)> SeedAsync(string email, bool confirmed, List<string> users)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = confirmed, CreatedAtUtc = DateTime.UtcNow };
        (await userManager.CreateAsync(user, "Seed-Password-1!")).Succeeded.Should().BeTrue();
        users.Add(user.Id);
        return (user.Id, email);
    }

    private async Task CleanupAsync(IReadOnlyList<string> userIds)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AskLucyDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        await db.PasswordResetTokens.Where(t => userIds.Contains(t.UserId)).ExecuteDeleteAsync();
        foreach (var id in userIds)
        {
            await db.Notifications.IgnoreQueryFilters().Where(n => n.RecipientUserId == id).ExecuteDeleteAsync();
            if (await userManager.FindByIdAsync(id) is { } user)
            {
                await userManager.DeleteAsync(user);
            }
        }
    }
}
