using System.Diagnostics;
using System.Text.RegularExpressions;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Notifications;
using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Domain.Notifications;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AskLucy.Web.Tests.Notifications;

/// <summary>One host whose only outgoing mail is a clock-stamped capture by a healthy fake mail server.</summary>
public sealed class NotificationLatencyFactory : CustomWebApplicationFactory
{
    public StampingMailServer MailServer { get; } = new(NotificationLatencyTests.Marker);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseMailServer(MailServer);
    }
}


/// <summary>
/// T234 — specs/067 SC-001 and SC-002, 200 events each (<c>NOTIFICATION_SCALE_FRACTION</c> shrinks the count).
/// <para>
/// Each event is a <c>document.storage.limit-reached</c> notification (a type with no related item to look up, so the measurement is the pipeline, not an access check; its in-app and email channels are both on by default) for one user,
/// published through the real <see cref="INotificationPublisher"/> and committed with the unit of work, so the real
/// outbox dispatcher, delivery worker and push path run unchanged in the host. Events arrive one every 360 ms, SC-005's sustained rate of 10,000 an hour, a
/// realistic trickle that keeps the measurement about latency, not about queueing behind a burst (the burst has its
/// own suite, <see cref="NotificationThroughputTests"/>). The clock starts just before the commit and stops:
/// </para>
/// <list type="bullet">
/// <item>SC-001: when a <c>notificationCreated</c> frame for the event reaches a SignalR client. The observer is a
/// real WebSocket connection to <c>/hubs/notifications</c> speaking the hub's JSON protocol, authenticated by the same
/// <c>access_token</c> parameter the browser uses (<see cref="HubPushListener"/>) — the whole server path is real, and
/// no SignalR client package is added to the test project. p95 must be under 5 s.</item>
/// <item>SC-002: when the delivery worker hands the email to the mail server (a healthy fake that accepts at once).
/// p95 must be under 2 minutes.</item>
/// </list>
/// Events are matched to their timings by a unique marker carried in the notification's <c>usedStorage</c> variable,
/// which the in-app message and the email both render. Gated: <c>RUN_SCALE_PERFORMANCE_TESTS=1</c>.
/// </summary>
[Collection(NotificationScaleTestGroup.Name)]
public sealed class NotificationLatencyTests(NotificationLatencyFactory factory, ITestOutputHelper output) : IClassFixture<NotificationLatencyFactory>
{
    internal static readonly Regex Marker = new(@"lat[0-9a-f]{8}x\d{3}", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private const int FullEvents = 200;
    private static readonly TimeSpan EventSpacing = TimeSpan.FromMilliseconds(360);

    [Fact(Skip = AccountEmailLatencyGate.SkipReason, SkipWhen = nameof(AccountEmailLatencyGate.NotRequested), SkipType = typeof(AccountEmailLatencyGate))]
    public async Task EventToNotificationCreatedPush_P95IsUnderFiveSeconds()
    {
        var ct = TestContext.Current.CancellationToken;
        var run = Guid.NewGuid().ToString("N")[..8];
        var users = await NotificationScaleTestSupport.SeedUsersAsync(factory.Services, "latp", run, 1);

        try
        {
            await using var listener = await HubPushListener.ConnectAsync(factory.Server, TestJwtFactory.Create(users[0]), Marker, ct);

            var committedAt = await PublishAsync(users[0], run, ct);
            var latencies = await CollectAsync(committedAt, listener.ReceivedAt, TimeSpan.FromMinutes(2), "notificationCreated pushes", ct);

            output.WriteLine($"SC-001 event to notificationCreated push, {latencies.Count} events: p50={NotificationScaleTestSupport.Percentile(latencies, 0.5):F3}s p95={NotificationScaleTestSupport.Percentile(latencies, 0.95):F3}s min={latencies.Min():F3}s max={latencies.Max():F3}s");
            NotificationScaleTestSupport.Percentile(latencies, 0.95).Should().BeLessThan(5, "SC-001: 95% of new in-app notifications reach an online user within 5 seconds");
        }
        finally
        {
            await NotificationScaleTestSupport.CleanupAsync(factory.Services, users, $"lat:{run}:");
        }
    }

    [Fact(Skip = AccountEmailLatencyGate.SkipReason, SkipWhen = nameof(AccountEmailLatencyGate.NotRequested), SkipType = typeof(AccountEmailLatencyGate))]
    public async Task EventToMailHandOff_P95IsUnderTwoMinutes_WithAHealthyMailServer()
    {
        var ct = TestContext.Current.CancellationToken;
        var run = Guid.NewGuid().ToString("N")[..8];
        var users = await NotificationScaleTestSupport.SeedUsersAsync(factory.Services, "latm", run, 1);

        try
        {
            var committedAt = await PublishAsync(users[0], run, ct);
            var latencies = await CollectAsync(committedAt, factory.MailServer.HandedOffAt, TimeSpan.FromMinutes(5), "mail hand-offs", ct);

            output.WriteLine($"SC-002 event to mail hand-off, {latencies.Count} events: p50={NotificationScaleTestSupport.Percentile(latencies, 0.5):F3}s p95={NotificationScaleTestSupport.Percentile(latencies, 0.95):F3}s min={latencies.Min():F3}s max={latencies.Max():F3}s");
            NotificationScaleTestSupport.Percentile(latencies, 0.95).Should().BeLessThan(120, "SC-002: 95% of emails reach the mail service within 2 minutes");
        }
        finally
        {
            await NotificationScaleTestSupport.CleanupAsync(factory.Services, users, $"lat:{run}:");
        }
    }

    /// <summary>Publishes and commits each event on its own unit of work; returns the clock reading taken just before each commit.</summary>
    private async Task<Dictionary<string, long>> PublishAsync(string userId, string run, CancellationToken ct)
    {
        var count = NotificationScaleTestSupport.Scaled(FullEvents, 20);
        var committedAt = new Dictionary<string, long>(count, StringComparer.Ordinal);
        var nextEvent = Stopwatch.GetTimestamp();

        for (var i = 0; i < count; i++)
        {
            var marker = $"lat{run}x{i:D3}";
            await using var scope = factory.Services.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<INotificationPublisher>().Publish(new NotificationRequest(
                NotificationTypeKeys.DocumentStorageLimitReached,
                new NotificationRecipient.User(userId),
                new Dictionary<string, string?> { ["usedStorage"] = marker, ["storageLimit"] = "10 GB" },
                EventKey: $"lat:{run}:{i}"));

            committedAt[marker] = Stopwatch.GetTimestamp();
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(ct);

            nextEvent += (long)(EventSpacing.TotalSeconds * Stopwatch.Frequency);
            var wait = Stopwatch.GetElapsedTime(Stopwatch.GetTimestamp(), nextEvent);
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, ct);
            }
        }

        return committedAt;
    }

    /// <summary>Waits until every event has been observed (or the deadline passes, which fails the test) and returns each one's latency in seconds.</summary>
    private static async Task<List<double>> CollectAsync(
        Dictionary<string, long> committedAt, System.Collections.Concurrent.ConcurrentDictionary<string, long> observedAt, TimeSpan deadline, string what, CancellationToken ct)
    {
        var until = Stopwatch.GetTimestamp() + (long)(deadline.TotalSeconds * Stopwatch.Frequency);
        while (committedAt.Keys.Any(m => !observedAt.ContainsKey(m)) && Stopwatch.GetTimestamp() < until)
        {
            await Task.Delay(100, ct);
        }

        var missing = committedAt.Keys.Count(m => !observedAt.ContainsKey(m));
        missing.Should().Be(0, $"every one of the {committedAt.Count} events should produce its {what} within {deadline.TotalMinutes:F0} minutes");

        return [.. committedAt.Select(e => Stopwatch.GetElapsedTime(e.Value, observedAt[e.Key]).TotalSeconds)];
    }
}
