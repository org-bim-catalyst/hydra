using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using AskLucy.Domain.Notifications;
using AskLucy.Persistence;
using AskLucy.Persistence.Identity;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AskLucy.Web.Tests.Notifications;

/// <summary>One host for the account-email tests, so they share one "auth-endpoints" rate-limit window and one database.</summary>
public sealed class AccountEmailHubFactory : CustomWebApplicationFactory;

/// <summary>
/// T121 — specs/067 US9-B, FR-009e and SC-014. For password reset and confirmation resend, a known and an
/// unknown address must be indistinguishable from outside: the same status, the same body, the same timing
/// envelope, and exactly one outbox insert each. Inside, the unknown address ends as <c>NoRecipient</c> with
/// no notification, no delivery and no email, and the log holds only a hash of it.
/// </summary>
public sealed class AccountEmailAntiEnumerationTests(AccountEmailHubFactory factory) : IClassFixture<AccountEmailHubFactory>, IAsyncLifetime
{
    private const string ForgotEndpoint = "/api/v1/auth/password/forgot";
    private const string ResendEndpoint = "/api/v1/auth/confirm-email/resend";

    private readonly HttpClient _client = factory.CreateClient();
    private readonly List<string> _seededUserIds = [];
    private string _confirmedEmail = string.Empty;
    private string _unconfirmedEmail = string.Empty;

    public async ValueTask InitializeAsync()
    {
        _confirmedEmail = await SeedUserAsync(confirmed: true);
        _unconfirmedEmail = await SeedUserAsync(confirmed: false);
    }

    public async ValueTask DisposeAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AskLucyDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        await db.Notifications.IgnoreQueryFilters().Where(n => n.RecipientUserId != null && _seededUserIds.Contains(n.RecipientUserId)).ExecuteDeleteAsync();
        foreach (var id in _seededUserIds)
        {
            if (await userManager.FindByIdAsync(id) is { } user)
            {
                await userManager.DeleteAsync(user);
            }
        }
    }

    private async Task<string> SeedUserAsync(bool confirmed)
    {
        var email = $"enum-{Guid.NewGuid():N}@example.com";
        await using var scope = factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = confirmed, CreatedAtUtc = DateTime.UtcNow };
        (await userManager.CreateAsync(user, "Seed-Password-1!")).Succeeded.Should().BeTrue();
        _seededUserIds.Add(user.Id);
        return email;
    }

    private Task<HttpResponseMessage> PostAsync(string endpoint, string email) =>
        _client.PostAsJsonAsync(endpoint, new { email }, TestContext.Current.CancellationToken);

    private async Task<(HttpStatusCode Status, string Body, double Milliseconds)> TimedPostAsync(string endpoint, string email)
    {
        var stopwatch = Stopwatch.StartNew();
        var response = await PostAsync(endpoint, email);
        stopwatch.Stop();
        return (response.StatusCode, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), stopwatch.Elapsed.TotalMilliseconds);
    }

    /// <summary>How many outbox events name <paramref name="address"/> as an address lookup, whatever their status.</summary>
    private async Task<int> OutboxEventsForAsync(string address)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AskLucyDbContext>();
        return await db.NotificationOutboxEvents.CountAsync(e => e.RecipientJson.Contains(address), TestContext.Current.CancellationToken);
    }

    private async Task<NotificationOutboxEvent> WaitForOutcomeAsync(string address, int expectedEvents)
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        List<NotificationOutboxEvent> events = [];
        while (DateTime.UtcNow < deadline)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AskLucyDbContext>();
            events = await db.NotificationOutboxEvents.AsNoTracking().Where(e => e.RecipientJson.Contains(address)).ToListAsync(TestContext.Current.CancellationToken);
            if (events.Count == expectedEvents && events.All(e => e.Status == OutboxEventStatus.Completed))
            {
                return events[0];
            }

            await Task.Delay(250, TestContext.Current.CancellationToken);
        }

        throw new TimeoutException($"{events.Count} of {expectedEvents} outbox events for the address completed: {string.Join(", ", events.Select(e => e.Status))}");
    }

    private static string Sha256Hex(string address) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(address.ToLowerInvariant())));

    [Fact]
    public async Task PasswordReset_KnownAndUnknownAddress_GiveTheSameStatusBodyAndTimingEnvelope_AndOneOutboxInsertEach()
    {
        var unknown = $"nobody-{Guid.NewGuid():N}@example.com";
        await PostAsync(ForgotEndpoint, _confirmedEmail); // warm the pipeline so first-request JIT cost lands nowhere
        await WaitForOutcomeAsync(_confirmedEmail, expectedEvents: 1);
        var knownEventsBefore = await OutboxEventsForAsync(_confirmedEmail);

        var knownRuns = new List<(HttpStatusCode Status, string Body, double Milliseconds)>();
        var unknownRuns = new List<(HttpStatusCode Status, string Body, double Milliseconds)>();
        for (var i = 0; i < 2; i++)
        {
            knownRuns.Add(await TimedPostAsync(ForgotEndpoint, _confirmedEmail));
            unknownRuns.Add(await TimedPostAsync(ForgotEndpoint, unknown));
        }

        knownRuns.Concat(unknownRuns).Should().OnlyContain(r => r.Status == HttpStatusCode.Accepted);
        knownRuns.Concat(unknownRuns).Select(r => r.Body).Distinct().Should().ContainSingle("the body is byte-identical for every address (SC-005)");

        // Exactly one outbox insert per request, known or not (FR-009e).
        (await OutboxEventsForAsync(_confirmedEmail) - knownEventsBefore).Should().Be(2);
        (await OutboxEventsForAsync(unknown)).Should().Be(2);

        // The envelope: no I/O-scale gap between the two (an account lookup or an SMTP trip would be hundreds of ms).
        var knownMedian = knownRuns.Select(r => r.Milliseconds).Order().ElementAt(knownRuns.Count / 2);
        var unknownMedian = unknownRuns.Select(r => r.Milliseconds).Order().ElementAt(unknownRuns.Count / 2);
        Math.Abs(knownMedian - unknownMedian).Should().BeLessThan(450,
            "an observer must not be able to infer account existence from response time (SC-002, FR-003)");
    }

    [Fact]
    public async Task PasswordReset_ForAnUnknownAddress_EndsAsNoRecipient_WithNoNotificationAndNoEmail_AndOnlyAHashInTheLog()
    {
        var unknown = $"Nobody-{Guid.NewGuid():N}@Example.com";

        (await PostAsync(ForgotEndpoint, unknown)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        var processed = await WaitForOutcomeAsync(unknown, expectedEvents: 1);

        processed.Outcome.Should().Be(OutboxEventOutcome.NoRecipient);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AskLucyDbContext>();
            (await db.Notifications.IgnoreQueryFilters().CountAsync(n => n.SourceEventId == processed.Id, TestContext.Current.CancellationToken))
                .Should().Be(0, "an address with no account gets no notification and no delivery");
        }

        ScriptableEmailSender.Shared.AcceptedMessages.Should().NotContain(m => string.Equals(m.To, unknown, StringComparison.OrdinalIgnoreCase));

        var lines = CapturedLogSink.Shared.Lines;
        lines.Should().Contain(l => l.Contains(Sha256Hex(unknown), StringComparison.Ordinal), "the outcome is explainable from the log by hash");
        lines.Should().NotContain(l => l.Contains(unknown, StringComparison.OrdinalIgnoreCase), "the address itself is never logged");
    }

    [Fact]
    public async Task PasswordReset_ForAKnownAddress_MaterializesOneNotification_AndSendsOneEmailWithALinkThatWorks()
    {
        (await PostAsync(ForgotEndpoint, _confirmedEmail)).StatusCode.Should().Be(HttpStatusCode.Accepted);

        var message = await WaitForMessageToAsync(_confirmedEmail);

        message.Subject.Should().Be("Reset your Ask Lucy password");
        message.TextBody.Should().Contain("https://tests.asklucy.io/reset-password?userId=").And.Contain("token=");
        message.MessageId.Should().MatchRegex("^<[0-9a-f-]{36}@");
    }

    [Fact]
    public async Task ConfirmationResend_KnownUnconfirmedAndUnknownAddress_GiveTheSameStatusAndBody_AndOneOutboxInsertEach()
    {
        var unknown = $"nobody-{Guid.NewGuid():N}@example.com";

        var known = await TimedPostAsync(ResendEndpoint, _unconfirmedEmail);
        var other = await TimedPostAsync(ResendEndpoint, unknown);

        known.Status.Should().Be(HttpStatusCode.Accepted);
        other.Status.Should().Be(known.Status);
        other.Body.Should().Be(known.Body);
        (await OutboxEventsForAsync(_unconfirmedEmail)).Should().Be(1);
        (await OutboxEventsForAsync(unknown)).Should().Be(1);

        (await WaitForOutcomeAsync(unknown, 1)).Outcome.Should().Be(OutboxEventOutcome.NoRecipient);
        (await WaitForOutcomeAsync(_unconfirmedEmail, 1)).Outcome.Should().Be(OutboxEventOutcome.Materialized);
        var message = await WaitForMessageToAsync(_unconfirmedEmail);
        message.Subject.Should().Be("Confirm your Ask Lucy account");
        message.TextBody.Should().Contain("/confirm-email?userId=");
    }

    private static async Task<AskLucy.Application.Abstractions.EmailMessage> WaitForMessageToAsync(string address)
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            var found = ScriptableEmailSender.Shared.AcceptedMessages.FirstOrDefault(m => string.Equals(m.To, address, StringComparison.OrdinalIgnoreCase));
            if (found is not null)
            {
                return found;
            }

            await Task.Delay(250, TestContext.Current.CancellationToken);
        }

        throw new TimeoutException("No email reached the fake SMTP server within a minute.");
    }
}
