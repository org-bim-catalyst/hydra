using System.Diagnostics;
using AskLucy.Application.Abstractions;
using AskLucy.Application.Notifications;
using AskLucy.Application.Notifications.Abstractions;
using AskLucy.Domain.Documents;
using AskLucy.Domain.Notifications;
using AskLucy.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AskLucy.Web.Tests.Notifications;

/// <summary>
/// One real host whose email send limit is set for the burst (the base factory switches the limiter off): a
/// general lane of a quarter of the burst a minute plus a reserved lane for mandatory mail, both derived from the
/// burst size so a reduced-scale run exercises the same shape. Everything else is the product default.
/// </summary>
public sealed class NotificationThroughputFactory : CustomWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Notifications:Email:MaxPerMinute"] = (NotificationThroughputTests.GeneralPerMinute + NotificationThroughputTests.ReservedPerMinute).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Notifications:Email:ReservedPerMinuteForMandatory"] = NotificationThroughputTests.ReservedPerMinute.ToString(System.Globalization.CultureInfo.InvariantCulture),
        }));
    }
}


/// <summary>
/// T232 — specs/067 SC-005 and the 500-document edge case. Gated: <c>RUN_SCALE_PERFORMANCE_TESTS=1</c>;
/// <c>NOTIFICATION_SCALE_FRACTION</c> shrinks every volume (the event counts, the send limit and the compressed hour).
/// <list type="number">
/// <item>Sustained load: 10,000 in-app-only notifications (<c>document.upload.completed</c>, email off by default, each pointing at a real document of its recipient so the access re-check runs) for
/// 50 users, published over a compressed hour (ten times faster than real time: 6 minutes). Once the stream ends the
/// in-app backlog (unprocessed outbox events and missing notifications) must empty within 10 minutes.</item>
/// <item>Burst: 500 <c>document.processing.failed</c> (in-app and email) for one user, then mandatory
/// <c>security.two-factor.enabled</c> notices for another, published right behind the burst, the worst case for
/// starvation. The in-app backlog must empty within 10 minutes of the burst ending.</item>
/// <item>Email: the send limit is the host's configuration (general lane = burst/4 a minute, plus a reserved mandatory
/// lane of burst/20). The 500 optional emails must all be handed over, never faster than the limit allows (the k-th
/// send is no earlier than the token bucket permits: k &lt;= lane + lane * elapsed / 1 minute) and the drain must finish
/// in about the time the limit implies. The mandatory emails must be handed over within SC-002's 2 minutes of being
/// published and before the optional backlog finishes, i.e. never held behind it.</item>
/// </list>
/// The mail server is the shared healthy fake SMTP server; hand-off times are the deliveries' own <c>SentAtUtc</c>.
/// </summary>
[Collection(NotificationScaleTestGroup.Name)]
public sealed class NotificationThroughputTests(NotificationThroughputFactory factory, ITestOutputHelper output) : IClassFixture<NotificationThroughputFactory>
{
    private const int FullStream = 10_000;
    private const int FullStreamUsers = 50;
    private const int FullBurst = 500;
    private const double CompressedHourSeconds = 360;

    private static int Burst => NotificationScaleTestSupport.Scaled(FullBurst, 5);

    /// <summary>The general (optional) email lane, a minute's worth: a quarter of the burst, so the burst takes about four minutes to drain.</summary>
    internal static int GeneralPerMinute => Math.Max(2, Burst / 4);

    /// <summary>The reserved lane mandatory mail draws on first (SC-014's lane, specs/067 R6); the mandatory notices equal it in number.</summary>
    internal static int ReservedPerMinute => Math.Max(1, Burst / 20);

    [Fact(Skip = AccountEmailLatencyGate.SkipReason, SkipWhen = nameof(AccountEmailLatencyGate.NotRequested), SkipType = typeof(AccountEmailLatencyGate))]
    public async Task TenThousandAnHour_AndA500DocumentBurst_EmptyTheInAppBacklog_DrainEmailAtTheLimit_AndNeverStarveMandatoryMail()
    {
        var ct = TestContext.Current.CancellationToken;
        var run = Guid.NewGuid().ToString("N")[..8];
        var prefix = $"thr:{run}:";
        var streamCount = NotificationScaleTestSupport.Scaled(FullStream, 100);
        var burst = Burst;
        var mandatoryCount = ReservedPerMinute;
        var general = GeneralPerMinute;

        var streamUsers = await NotificationScaleTestSupport.SeedUsersAsync(factory.Services, "thrs", run, NotificationScaleTestSupport.Scaled(FullStreamUsers, 5));
        var burstUser = (await NotificationScaleTestSupport.SeedUsersAsync(factory.Services, "thrb", run, 1))[0];
        var mandatoryUser = (await NotificationScaleTestSupport.SeedUsersAsync(factory.Services, "thrm", run, 1))[0];
        var allUsers = streamUsers.Append(burstUser).Append(mandatoryUser).ToList();

        try
        {
            // ---- 1. sustained load over a compressed hour ----
            var duration = TimeSpan.FromSeconds(Math.Max(5, CompressedHourSeconds * NotificationScaleTestSupport.Fraction));
            var streamDocuments = await SeedStreamDocumentsAsync(streamUsers, ct);
            await PublishStreamAsync(streamUsers, streamDocuments, prefix, streamCount, duration, ct);
            var streamDrain = await WaitForInAppBacklogAsync(allUsers, prefix, streamCount, TimeSpan.FromMinutes(10), ct);
            output.WriteLine($"SC-005 stream: {streamCount:N0} notifications over {duration.TotalSeconds:F0}s (a compressed hour); in-app backlog empty {streamDrain.TotalSeconds:F1}s after the last one.");

            // ---- 2. the burst, with mandatory mail right behind it ----
            var documents = await SeedDocumentsAsync(burstUser, burst, ct);
            var burstStarted = DateTime.UtcNow;
            await PublishBurstAsync(burstUser, prefix, documents, ct);
            var mandatoryPublished = DateTime.UtcNow;
            await PublishMandatoryAsync(mandatoryUser, prefix, mandatoryCount, ct);
            var burstEnded = DateTime.UtcNow;

            var inAppDrain = await WaitForInAppBacklogAsync(allUsers, prefix, streamCount + burst + mandatoryCount, TimeSpan.FromMinutes(10), ct);
            output.WriteLine($"SC-005 burst: {burst} document.processing.failed + {mandatoryCount} mandatory published in {(burstEnded - burstStarted).TotalSeconds:F1}s; in-app backlog empty {inAppDrain.TotalSeconds:F1}s after the burst ended.");

            // ---- 3. email drains at the configured limit ----
            var expectedDrain = TimeSpan.FromMinutes(Math.Max(0, burst - general) / (double)general);
            var emailDeadline = expectedDrain + TimeSpan.FromMinutes(4);
            await WaitForEmailsAsync(burstUser, burst, mandatoryUser, mandatoryCount, emailDeadline, ct);

            var optionalSent = (await SentTimesAsync(burstUser, ct)).Order().ToList();
            var mandatorySent = (await SentTimesAsync(mandatoryUser, ct)).Order().ToList();
            optionalSent.Should().HaveCount(burst);
            mandatorySent.Should().HaveCount(mandatoryCount);

            var drained = optionalSent[^1] - burstStarted;
            output.WriteLine($"SC-005 email: limit {general}/min general + {ReservedPerMinute}/min reserved; {burst} optional emails drained in {drained.TotalSeconds:F0}s (the limit implies about {expectedDrain.TotalSeconds:F0}s); " +
                             $"mandatory last hand-off {(mandatorySent[^1] - mandatoryPublished).TotalSeconds:F1}s after publication.");

            // Never faster than the limit: the k-th send (1-based) cannot precede what the bucket has allowed since the burst began.
            for (var k = 1; k <= optionalSent.Count; k++)
            {
                var elapsedMinutes = Math.Max(0, (optionalSent[k - 1] - burstStarted).TotalMinutes);
                k.Should().BeLessThanOrEqualTo((int)Math.Ceiling(general + general * elapsedMinutes) + 1,
                    $"send #{k} at +{elapsedMinutes * 60:F1}s must respect the {general}/min limit");
            }

            // ...and it drains in about the time the limit implies (the bound has slack for the worker's polling).
            drained.Should().BeLessThan(expectedDrain + TimeSpan.FromMinutes(3), "the email backlog drains at the configured limit");

            // Mandatory mail is never held behind optional mail.
            foreach (var sent in mandatorySent)
            {
                (sent - mandatoryPublished).Should().BeLessThan(TimeSpan.FromMinutes(2), "SC-002/SC-005: a mandatory email is not delayed by the optional backlog");
            }

            if (burst > general)
            {
                mandatorySent[^1].Should().BeBefore(optionalSent[^1], "mandatory emails, published behind the burst, finish before the optional backlog does");
            }

            await AssertNothingLostAsync(allUsers, prefix, streamCount + burst + mandatoryCount, ct);
            streamDrain.Should().BeLessThan(TimeSpan.FromMinutes(10), "SC-005: the in-app backlog empties within 10 minutes of the stream ending");
            inAppDrain.Should().BeLessThan(TimeSpan.FromMinutes(10), "SC-005: the in-app backlog empties within 10 minutes of the burst ending");
        }
        finally
        {
            await DeleteDocumentsAsync(allUsers);
            await NotificationScaleTestSupport.CleanupAsync(factory.Services, allUsers, prefix);
        }
    }

    // ---- publishing ----

    /// <summary>Publishes the stream in 100 batches, one commit each, spaced evenly over <paramref name="duration"/>.</summary>
    private async Task PublishStreamAsync(List<string> users, Dictionary<string, Guid> documents, string prefix, int count, TimeSpan duration, CancellationToken ct)
    {
        const int batches = 100;
        var started = Stopwatch.GetTimestamp();
        var published = 0;
        for (var b = 0; b < batches; b++)
        {
            var upTo = (int)((long)count * (b + 1) / batches);
            await using var scope = factory.Services.CreateAsyncScope();
            var publisher = scope.ServiceProvider.GetRequiredService<INotificationPublisher>();
            for (; published < upTo; published++)
            {
                publisher.Publish(new NotificationRequest(
                    NotificationTypeKeys.DocumentUploadCompleted,
                    new NotificationRecipient.User(users[published % users.Count]),
                    new Dictionary<string, string?> { ["documentName"] = $"Site photos {published}.pdf" },
                    RelatedItem: new RelatedItem("Document", documents[users[published % users.Count]].ToString()),
                    EventKey: $"{prefix}s:{published}"));
            }

            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(ct);

            var due = TimeSpan.FromTicks(duration.Ticks * (b + 1) / batches) - Stopwatch.GetElapsedTime(started);
            if (due > TimeSpan.Zero)
            {
                await Task.Delay(due, ct);
            }
        }
    }

    /// <summary>
    /// Seeds the documents a bulk upload would have created, so the dispatcher's access re-check (R24) finds a real,
    /// owned document for each event, as in production. They are removed with the run (see the finally block).
    /// </summary>
    private async Task<Dictionary<string, Guid>> SeedStreamDocumentsAsync(IReadOnlyList<string> owners, CancellationToken ct)
    {
        var byOwner = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var owner in owners)
        {
            byOwner[owner] = (await SeedDocumentsAsync(owner, 1, ct))[0];
        }

        return byOwner;
    }

    private async Task<List<Guid>> SeedDocumentsAsync(string ownerId, int count, CancellationToken ct)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AskLucyDbContext>();
        var documents = Enumerable.Range(0, count)
            .Select(i => Document.Create(Guid.CreateVersion7(), ownerId, $"Drawing {i:D4}.pdf", DocumentFileType.Pdf, 1024, Guid.CreateVersion7(), "scale-seed"))
            .ToList();
        db.Documents.AddRange(documents);
        await db.SaveChangesAsync(ct);
        return documents.Select(d => d.Id).ToList();
    }

    private async Task DeleteDocumentsAsync(List<string> ownerIds)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AskLucyDbContext>();
        await db.Documents.IgnoreQueryFilters().Where(d => ownerIds.Contains(d.OwnerId)).ExecuteDeleteAsync(CancellationToken.None);
    }

    private async Task PublishBurstAsync(string userId, string prefix, List<Guid> documents, CancellationToken ct)
    {
        var count = documents.Count;
        for (var from = 0; from < count; from += 100)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var publisher = scope.ServiceProvider.GetRequiredService<INotificationPublisher>();
            for (var i = from; i < Math.Min(count, from + 100); i++)
            {
                publisher.Publish(new NotificationRequest(
                    NotificationTypeKeys.DocumentProcessingFailed,
                    new NotificationRecipient.User(userId),
                    new Dictionary<string, string?> { ["documentName"] = $"Drawing {i:D4}.pdf", ["failureSummary"] = "the file could not be read" },
                    RelatedItem: new RelatedItem("Document", documents[i].ToString()),
                    EventKey: $"{prefix}b:{i}"));
            }

            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(ct);
        }
    }

    private async Task PublishMandatoryAsync(string userId, string prefix, int count, CancellationToken ct)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var publisher = scope.ServiceProvider.GetRequiredService<INotificationPublisher>();
        for (var i = 0; i < count; i++)
        {
            publisher.Publish(new NotificationRequest(
                NotificationTypeKeys.SecurityTwoFactorEnabled,
                new NotificationRecipient.User(userId),
                new Dictionary<string, string?> { ["changedAt"] = "2026-10-07 09:00 UTC" },
                EventKey: $"{prefix}m:{i}"));
        }

        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(ct);
    }

    // ---- waiting and reading ----

    /// <summary>
    /// Waits until every outbox event of the run is processed and <paramref name="expected"/> notifications exist for the
    /// run's users, and returns how long that took. A deadline passing fails the test with the counts it saw.
    /// </summary>
    private async Task<TimeSpan> WaitForInAppBacklogAsync(IReadOnlyList<string> users, string prefix, int expected, TimeSpan limit, CancellationToken ct)
    {
        var started = Stopwatch.GetTimestamp();
        (int Notifications, int Unprocessed) last = (0, 0);
        while (Stopwatch.GetElapsedTime(started) < limit)
        {
            last = await QueryAsync(async db => (
                await db.Notifications.IgnoreQueryFilters().CountAsync(n => n.RecipientUserId != null && users.Contains(n.RecipientUserId), ct),
                await db.NotificationOutboxEvents.CountAsync(e => e.EventKey != null && e.EventKey.StartsWith(prefix) && e.Status != OutboxEventStatus.Completed, ct)));

            if (last.Notifications >= expected && last.Unprocessed == 0)
            {
                return Stopwatch.GetElapsedTime(started);
            }

            await Task.Delay(1000, ct);
        }

        throw new TimeoutException($"After {limit.TotalMinutes:F0} minutes {last.Notifications} of {expected} notifications existed and {last.Unprocessed} outbox events were still unprocessed.");
    }

    private async Task WaitForEmailsAsync(string burstUser, int burst, string mandatoryUser, int mandatory, TimeSpan limit, CancellationToken ct)
    {
        var started = Stopwatch.GetTimestamp();
        (int Optional, int Mandatory) last = (0, 0);
        while (Stopwatch.GetElapsedTime(started) < limit)
        {
            last = ((await SentTimesAsync(burstUser, ct)).Count, (await SentTimesAsync(mandatoryUser, ct)).Count);
            if (last.Optional >= burst && last.Mandatory >= mandatory)
            {
                return;
            }

            await Task.Delay(2000, ct);
        }

        throw new TimeoutException($"After {limit.TotalMinutes:F1} minutes {last.Optional} of {burst} optional and {last.Mandatory} of {mandatory} mandatory emails had been handed over. Deliveries: {await BreakdownAsync(burstUser, mandatoryUser)}");
    }

    private Task<List<DateTime>> SentTimesAsync(string userId, CancellationToken ct) => QueryAsync(async db =>
    {
        var times = await db.Set<NotificationDelivery>().AsNoTracking()
            .Where(d => d.Channel == NotificationChannel.Email && d.Status == DeliveryStatus.Sent && d.SentAtUtc != null
                && db.Notifications.IgnoreQueryFilters().Any(n => n.Id == d.NotificationId && n.RecipientUserId == userId))
            .Select(d => d.SentAtUtc!.Value)
            .ToListAsync(ct);
        return times.Select(t => DateTime.SpecifyKind(t, DateTimeKind.Utc)).ToList();
    });

    private Task<string> BreakdownAsync(string burstUser, string mandatoryUser) => QueryAsync(async db => string.Join("; ", (await db.Set<NotificationDelivery>().AsNoTracking()
        .Where(d => db.Notifications.IgnoreQueryFilters().Any(n => n.Id == d.NotificationId && (n.RecipientUserId == burstUser || n.RecipientUserId == mandatoryUser)))
        .GroupBy(d => new { d.Channel, d.Status, d.FailureKind })
        .Select(g => new { g.Key.Channel, g.Key.Status, g.Key.FailureKind, Count = g.Count() })
        .ToListAsync(CancellationToken.None)).Select(g => $"{g.Count}x {g.Channel}/{g.Status}/{g.FailureKind}")));

    /// <summary>Every event became exactly one notification, every in-app copy was delivered, and nothing failed.</summary>
    private async Task AssertNothingLostAsync(IReadOnlyList<string> users, string prefix, int expected, CancellationToken ct)
    {
        var (notifications, undelivered, outboxNotCompleted, failedDeliveries) = await QueryAsync(async db => (
            await db.Notifications.IgnoreQueryFilters().CountAsync(n => n.RecipientUserId != null && users.Contains(n.RecipientUserId), ct),
            await db.Set<NotificationDelivery>().CountAsync(d => d.Channel == NotificationChannel.InApp && d.Status != DeliveryStatus.Delivered
                && db.Notifications.IgnoreQueryFilters().Any(n => n.Id == d.NotificationId && n.RecipientUserId != null && users.Contains(n.RecipientUserId)), ct),
            await db.NotificationOutboxEvents.CountAsync(e => e.EventKey != null && e.EventKey.StartsWith(prefix) && e.Status != OutboxEventStatus.Completed, ct),
            await db.Set<NotificationDelivery>().CountAsync(d => (d.Status == DeliveryStatus.Failed || d.Status == DeliveryStatus.DeadLettered)
                && db.Notifications.IgnoreQueryFilters().Any(n => n.Id == d.NotificationId && n.RecipientUserId != null && users.Contains(n.RecipientUserId)), ct)));

        notifications.Should().Be(expected, "every published event became exactly one notification");
        undelivered.Should().Be(0, "every in-app copy was delivered");
        outboxNotCompleted.Should().Be(0);
        failedDeliveries.Should().Be(0, "a healthy mail server and a limit that only defers fail nothing");
    }

    private async Task<T> QueryAsync<T>(Func<AskLucyDbContext, Task<T>> query)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<AskLucyDbContext>());
    }
}
