using AskLucy.Domain.Notifications;
using AskLucy.Persistence.Repositories;
using FluentAssertions;

namespace AskLucy.Persistence.Tests.Notifications;

/// <summary>
/// T153 — specs/067 US6, research R21: the dashboard's numbers are database aggregates. Every test seeds into a window of its own, far
/// in the past and chosen at random, so rows other tests left behind never fall inside it; only the live backlog and unread counts are
/// global, and those are asserted as differences.
/// </summary>
[Collection(PersistenceTestCollection.Name)]
public sealed class NotificationStatisticsQueryTests(PersistenceTestFixture fixture)
{
    private static readonly Random Random = new();
    private static readonly NotificationTypeDefinition Failed = NotificationTypeCatalog.Get(NotificationTypeKeys.WorkflowExecutionFailed);
    private static readonly NotificationTypeDefinition Document = NotificationTypeCatalog.Get(NotificationTypeKeys.DocumentProcessingFailed);

    /// <summary>A day nobody else seeds into: a random one in 2020-2024, which no run's "now" ever reaches.</summary>
    private static DateTime NewWindowStart() => new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(Random.Next(0, 1800));

    private async Task<string> SeedUserAsync(CancellationToken ct)
    {
        var userId = $"stats-{Guid.NewGuid():N}";
        await using var context = fixture.CreateDbContext();
        context.Users.Add(PersistenceTestFixture.CreateTestUser(userId));
        await context.SaveChangesAsync(ct);
        return userId;
    }

    private async Task<NotificationStatisticsDataResult> StatisticsAsync(DateTime from, DateTime to, bool hourly, CancellationToken ct)
    {
        await using var context = fixture.CreateDbContext();
        var data = await new NotificationAdminRepository(context).GetStatisticsAsync(from, to, hourly, DateTime.UtcNow, ct);
        return new NotificationStatisticsDataResult(data);
    }

    private sealed record NotificationStatisticsDataResult(AskLucy.Application.Notifications.Abstractions.NotificationStatisticsData Data);

    /// <summary>A notification created at <paramref name="at"/> with one email delivery that ends as described.</summary>
    private async Task SeedAsync(
        string userId, DateTime at, NotificationTypeDefinition definition, Func<NotificationDelivery, DateTime, NotificationDelivery> shape, CancellationToken ct,
        NotificationChannel channel = NotificationChannel.Email)
    {
        var correlation = $"stats-{Guid.NewGuid():N}";
        var notification = Notification.Create(
            userId, definition, NotificationPriority.Normal, string.Empty, string.Empty, "en", correlation, at, showInCenter: false, eventKey: correlation);
        var delivery = NotificationDelivery.CreatePending(channel, NotificationPriority.Normal, RecipientKind.User, null, 5, null, correlation, at);
        notification.AddDelivery(shape(delivery, at));
        await using var context = fixture.CreateDbContext();
        context.Notifications.Add(notification);
        await context.SaveChangesAsync(ct);
    }

    private static NotificationDelivery Sent(NotificationDelivery d, DateTime at, TimeSpan after)
    {
        d.MarkSending("w", at.AddMinutes(2), at);
        d.MarkSent("en", null, "250 OK", at + after);
        return d;
    }

    private static NotificationDelivery FailedWith(NotificationDelivery d, DateTime at, DeliveryFailureKind kind)
    {
        d.MarkSending("w", at.AddMinutes(2), at);
        d.Fail(kind, "Rejected.", "550", at);
        return d;
    }

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task CountsCreatedSentFailedDeadLetteredAndAmbiguous_InsideTheWindowOnly()
    {
        var ct = TestContext.Current.CancellationToken;
        var start = NewWindowStart();
        var user = await SeedUserAsync(ct);
        await SeedAsync(user, start.AddHours(1), Failed, (d, at) => Sent(d, at, TimeSpan.FromSeconds(3)), ct);
        await SeedAsync(user, start.AddHours(1), Failed, (d, at) => Sent(d, at, TimeSpan.FromSeconds(5)), ct);
        await SeedAsync(user, start.AddHours(2), Failed, (d, at) => FailedWith(d, at, DeliveryFailureKind.Permanent), ct);
        await SeedAsync(user, start.AddHours(2), Failed, (d, at) => FailedWith(d, at, DeliveryFailureKind.AmbiguousOutcome), ct);
        await SeedAsync(user, start.AddHours(3), Failed, (d, at) => { d.MarkSending("w", at.AddMinutes(2), at); d.DeadLetter("Retry limit reached."); return d; }, ct);
        // Outside the window: three days later.
        await SeedAsync(user, start.AddDays(3), Failed, (d, at) => Sent(d, at, TimeSpan.FromSeconds(1)), ct);

        var stats = (await StatisticsAsync(start, start.AddDays(1), hourly: true, ct)).Data;

        stats.Created.Should().Be(5);
        stats.Sent.Should().Be(2);
        stats.Failed.Should().Be(2, "a delivery that failed for good, and one with an unknown outcome");
        stats.DeadLettered.Should().Be(1);
        stats.Ambiguous.Should().Be(1);
    }

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task EmailSuccessRate_IsBasedOnEmailOutcomesOnly()
    {
        var ct = TestContext.Current.CancellationToken;
        var start = NewWindowStart();
        var user = await SeedUserAsync(ct);
        await SeedAsync(user, start.AddHours(1), Failed, (d, at) => Sent(d, at, TimeSpan.FromSeconds(1)), ct);
        await SeedAsync(user, start.AddHours(1), Failed, (d, at) => Sent(d, at, TimeSpan.FromSeconds(1)), ct);
        await SeedAsync(user, start.AddHours(1), Failed, (d, at) => Sent(d, at, TimeSpan.FromSeconds(1)), ct);
        await SeedAsync(user, start.AddHours(1), Failed, (d, at) => FailedWith(d, at, DeliveryFailureKind.Permanent), ct);

        var stats = (await StatisticsAsync(start, start.AddDays(1), hourly: false, ct)).Data;

        stats.EmailSent.Should().Be(3);
        stats.EmailFailed.Should().Be(1);
    }

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task AverageAndP95Latency_AreMeasuredFromCreationToSend()
    {
        var ct = TestContext.Current.CancellationToken;
        var start = NewWindowStart();
        var user = await SeedUserAsync(ct);
        for (var seconds = 1; seconds <= 20; seconds++)
        {
            var latency = TimeSpan.FromSeconds(seconds);
            await SeedAsync(user, start.AddHours(1), Failed, (d, at) => Sent(d, at, latency), ct);
        }

        var stats = (await StatisticsAsync(start, start.AddDays(1), hourly: true, ct)).Data;

        stats.AverageLatencyMs.Should().Be(10_500, "the mean of 1 s to 20 s");
        stats.P95LatencyMs.Should().Be(19_000, "the 19th of 20 ordered values");
    }

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task LatencyIsNull_WhenNothingWasSent()
    {
        var ct = TestContext.Current.CancellationToken;
        var start = NewWindowStart();

        var stats = (await StatisticsAsync(start, start.AddDays(1), hourly: true, ct)).Data;

        stats.AverageLatencyMs.Should().BeNull();
        stats.P95LatencyMs.Should().BeNull();
        stats.Created.Should().Be(0);
        stats.Series.Should().BeEmpty();
    }

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task Retries_CountEveryAttemptAfterTheFirst()
    {
        var ct = TestContext.Current.CancellationToken;
        var start = NewWindowStart();
        var user = await SeedUserAsync(ct);
        await SeedAsync(user, start.AddHours(1), Failed, (d, at) =>
        {
            d.MarkSending("w", at.AddMinutes(2), at);
            d.ScheduleRetry(at.AddMinutes(1), DeliveryFailureKind.Transient, "Busy.", "451", at);
            d.MarkSending("w", at.AddMinutes(3), at);
            d.ScheduleRetry(at.AddMinutes(5), DeliveryFailureKind.Transient, "Busy.", "451", at);
            d.MarkSending("w", at.AddMinutes(7), at);
            d.MarkSent("en", null, "250 OK", at.AddMinutes(8));
            return d;
        }, ct);

        var stats = (await StatisticsAsync(start, start.AddDays(1), hourly: true, ct)).Data;

        stats.Retries.Should().Be(2, "three attempts is two retries");
    }

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task ByCategory_SplitsCreatedAndFailed()
    {
        var ct = TestContext.Current.CancellationToken;
        var start = NewWindowStart();
        var user = await SeedUserAsync(ct);
        await SeedAsync(user, start.AddHours(1), Failed, (d, at) => FailedWith(d, at, DeliveryFailureKind.Permanent), ct);
        await SeedAsync(user, start.AddHours(1), Failed, (d, at) => Sent(d, at, TimeSpan.FromSeconds(1)), ct);
        await SeedAsync(user, start.AddHours(1), Document, (d, at) => Sent(d, at, TimeSpan.FromSeconds(1)), ct);

        var stats = (await StatisticsAsync(start, start.AddDays(1), hourly: true, ct)).Data;

        stats.ByCategory.Should().ContainSingle(c => c.Category == NotificationCategory.Workflow).Which.Should().Match<AskLucy.Application.Notifications.Abstractions.CategoryCountRow>(
            c => c.Created == 2 && c.Failed == 1);
        stats.ByCategory.Should().ContainSingle(c => c.Category == NotificationCategory.Document).Which.Should().Match<AskLucy.Application.Notifications.Abstractions.CategoryCountRow>(
            c => c.Created == 1 && c.Failed == 0);
    }

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task HourlyBuckets_GroupByWholeHoursFromTheWindowStart()
    {
        var ct = TestContext.Current.CancellationToken;
        var start = NewWindowStart();
        var user = await SeedUserAsync(ct);
        await SeedAsync(user, start.AddMinutes(10), Failed, (d, at) => Sent(d, at, TimeSpan.FromSeconds(1)), ct);
        await SeedAsync(user, start.AddMinutes(50), Failed, (d, at) => Sent(d, at, TimeSpan.FromSeconds(1)), ct);
        await SeedAsync(user, start.AddHours(2).AddMinutes(5), Failed, (d, at) => FailedWith(d, at, DeliveryFailureKind.Permanent), ct);

        var stats = (await StatisticsAsync(start, start.AddDays(1), hourly: true, ct)).Data;

        stats.Series.Should().HaveCount(2);
        stats.Series[0].Should().Be(new AskLucy.Application.Notifications.Abstractions.SeriesRow(start, Created: 2, Sent: 2, Failed: 0));
        stats.Series[1].Should().Be(new AskLucy.Application.Notifications.Abstractions.SeriesRow(start.AddHours(2), Created: 1, Sent: 0, Failed: 1));
    }

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task DailyBuckets_GroupByWholeDaysFromTheWindowStart()
    {
        var ct = TestContext.Current.CancellationToken;
        var start = NewWindowStart();
        var user = await SeedUserAsync(ct);
        await SeedAsync(user, start.AddHours(3), Failed, (d, at) => Sent(d, at, TimeSpan.FromSeconds(1)), ct);
        await SeedAsync(user, start.AddHours(20), Failed, (d, at) => Sent(d, at, TimeSpan.FromSeconds(1)), ct);
        await SeedAsync(user, start.AddDays(2).AddHours(1), Failed, (d, at) => Sent(d, at, TimeSpan.FromSeconds(1)), ct);

        var stats = (await StatisticsAsync(start, start.AddDays(7), hourly: false, ct)).Data;

        stats.Series.Select(s => (s.BucketStartUtc, s.Created)).Should().Equal((start, 2), (start.AddDays(2), 1));
    }

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task Backlog_CountsOnlyWhatIsDueNow_AndNamesTheOldest()
    {
        var ct = TestContext.Current.CancellationToken;
        var window = NewWindowStart();
        var before = (await StatisticsAsync(window, window.AddDays(1), hourly: true, ct)).Data;
        var user = await SeedUserAsync(ct);
        var now = DateTime.UtcNow;
        await SeedAsync(user, now.AddMinutes(-9), Failed, (d, _) => d, ct);
        await SeedAsync(user, now.AddMinutes(-2), Failed, (d, _) => d, ct);
        await SeedAsync(user, now.AddMinutes(40), Failed, (d, _) => d, ct);

        var after = (await StatisticsAsync(window, window.AddDays(1), hourly: true, ct)).Data;

        (after.DeliveriesDue - before.DeliveriesDue).Should().Be(2, "a delivery due in forty minutes isn't waiting yet");
        after.OldestDueAtUtc.Should().BeOnOrBefore(now.AddMinutes(-9).AddSeconds(1));
    }

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task OutboxPending_CountsEventsStillWaitingToBeDispatched()
    {
        var ct = TestContext.Current.CancellationToken;
        var window = NewWindowStart();
        var before = (await StatisticsAsync(window, window.AddDays(1), hourly: true, ct)).Data;
        await using (var context = fixture.CreateDbContext())
        {
            context.NotificationOutboxEvents.Add(NotificationOutboxEvent.Create(
                NotificationTypeKeys.WorkflowExecutionFailed, """{"kind":"User","userId":"x"}""", "{}", "stats-outbox", DateTime.UtcNow.AddMinutes(-1)));
            await context.SaveChangesAsync(ct);
        }

        var after = (await StatisticsAsync(window, window.AddDays(1), hourly: true, ct)).Data;

        (after.OutboxPending - before.OutboxPending).Should().Be(1);
    }

    [Fact(Skip = PersistenceDatabaseGate.SkipReason, SkipWhen = nameof(PersistenceDatabaseGate.NotConfigured), SkipType = typeof(PersistenceDatabaseGate))]
    public async Task Unread_CountsCenterNotificationsNotYetRead()
    {
        var ct = TestContext.Current.CancellationToken;
        var window = NewWindowStart();
        var before = (await StatisticsAsync(window, window.AddDays(1), hourly: true, ct)).Data;
        var user = await SeedUserAsync(ct);
        await using (var context = fixture.CreateDbContext())
        {
            var now = DateTime.UtcNow;
            var correlation = $"stats-unread-{Guid.NewGuid():N}";
            var notification = Notification.Create(user, Failed, NotificationPriority.Normal, "Title", "Message", "en", correlation, now, showInCenter: true, eventKey: correlation);
            notification.AddDelivery(NotificationDelivery.CreateDelivered(NotificationChannel.InApp, NotificationPriority.Normal, "en", null, correlation, now));
            context.Notifications.Add(notification);
            await context.SaveChangesAsync(ct);
        }

        var after = (await StatisticsAsync(window, window.AddDays(1), hourly: true, ct)).Data;

        (after.UnreadNotifications - before.UnreadNotifications).Should().Be(1);
    }
}
